using Microsoft.Extensions.Logging;
using Action = UvA.Workflow.Entities.Domain.Action;

namespace UvA.Workflow.Jobs;

public record JobInput(MailMessage? Mail);

public class JobService(
    EffectService effectService,
    ModelService modelService,
    IJobRepository repository,
    ILogger logger)
{
    public async Task<EffectResult> CreateAndRunJob(WorkflowInstance instance, Action action, User user,
        JobInput? input, CancellationToken ct)
    {
        // TODO: support form submission? Or refactor that so it action-based as well (probably a batter idea)
        var job = new Job
        {
            Action = action.Name ?? throw new InvalidOperationException("Invalid action"),
            CreatedBy = user.Id,
            StartOn = DateTime.Now,
            InstanceId = instance.Id,
            Input = input
        };

        var result = await RunJob(job, instance, action, user, ct);
        if (job.Steps.Count > 0)
            await repository.Add(job, ct);
        if (job.Status == JobStatus.Failed)
            throw new Exception($"Job {job.Id} failed");
        return result;
    }

    private async Task<EffectResult> RunJob(Job job, WorkflowInstance instance, Action action, User user,
        CancellationToken ct)
    {
        var context = modelService.CreateContext(instance);
        EffectResult result = new();
        foreach (var effect in action.OnAction.Where(t => t.Condition.IsMet(context)))
        {
            var step = new JobStep { Identifier = effect.Identifier };
            if (effect.IsLogged)
                job.Steps.Add(step);
            try
            {
                result = await effectService.RunEffect(job.Input, instance, effect, user, context, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running effect {Effect}", effect.Identifier);
                step.Message = ex.ToString();
                job.Status = JobStatus.Failed;
#if DEBUG
                throw;
#endif
                return result;
            }

            var outputs = context.Get(effect.Name ?? effect.ServiceCall?.Operation ?? "__invalid")
                as Dictionary<Lookup, object>;
            step.Outputs = outputs?.ToDictionary(o => o.Key.ToString(), o => o.Value);
        }

        job.Status = JobStatus.Completed;
        job.ExecutedOn = DateTime.Now;
        return result;
    }
}