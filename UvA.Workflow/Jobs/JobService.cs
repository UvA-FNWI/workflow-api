using Microsoft.Extensions.Logging;
using Action = UvA.Workflow.Entities.Domain.Action;

namespace UvA.Workflow.Jobs;

public record JobInput(MailMessage? Mail);

public class JobService(
    EffectService effectService,
    ModelService modelService,
    IJobRepository repository,
    IWorkflowInstanceRepository workflowInstanceRepository,
    IUserRepository userRepository,
    ILogger<JobService> logger,
    InstanceService instanceService)
{
    public Task<EffectResult> CreateAndRunJob(WorkflowInstance instance, Action action, User user,
        JobInput? input, CancellationToken ct)
        => CreateAndRunJob(instance, JobSource.Action,
            action.Name ?? throw new InvalidOperationException("Invalid action"), action.OnAction, user, input, ct);

    public async Task<EffectResult> CreateAndRunJob(WorkflowInstance instance, JobSource sourceType, string sourceName,
        Effect[] effects, User user, JobInput? input, CancellationToken ct)
    {
        var steps = effects
            .Where(e => e.Delay == null)
            .ToDictionary(e => new JobStep { Identifier = e.Identifier }, e => e);
        var job = new Job
        {
            SourceType = sourceType,
            SourceName = sourceName,
            CreatedBy = user.Id,
            StartOn = DateTime.Now,
            InstanceId = instance.Id,
            Input = input,
            IsSynchronous = true,
            Steps = steps.Keys.ToList()
        };

        var result = await RunJob(job, instance, effects, user, ct);
        job.Steps = job.Steps.Where(s => steps[s].IsLogged).ToList();
        if (job.Steps.Count > 0)
            await repository.Add(job, ct);
        if (job.Status == JobStatus.Failed)
            throw new Exception($"Job {job.Id} failed");

        foreach (var delayGroup in effects
                     .Where(e => e.DelayAsTimeSpan != null)
                     .GroupBy(e => e.DelayAsTimeSpan!.Value))
            await repository.Add(new Job
            {
                SourceType = sourceType,
                SourceName = sourceName,
                CreatedBy = user.Id,
                StartOn = DateTime.Now.Add(delayGroup.Key),
                InstanceId = instance.Id,
                Input = input,
                IsSynchronous = false,
                Steps = delayGroup.Select(e => new JobStep { Identifier = e.Identifier }).ToList()
            }, ct);

        return result;
    }

    public async Task RunJob(Job job, CancellationToken ct)
    {
        var instance = await workflowInstanceRepository.GetById(job.InstanceId, ct);
        if (instance == null)
        {
            logger.LogError("Job {Job} references non-existing instance {InstanceId}", job.Id, job.InstanceId);
            throw new Exception("Instance not found");
        }

        // TODO: allow jobs without a user
        var user = await userRepository.GetById(job.CreatedBy!, ct) ?? throw new Exception();
        var effects = job.SourceType switch
        {
            JobSource.Action => modelService.WorkflowDefinitions[instance.WorkflowDefinition]
                .AllActions.Single(a => a.Name == job.SourceName).OnAction,
            JobSource.Submit => modelService.WorkflowDefinitions[instance.WorkflowDefinition]
                .Forms.Single(f => f.Name == job.SourceName).OnSubmit,
            _ => throw new NotImplementedException()
        };
        await RunJob(job, instance, effects, user, ct);
        await repository.Update(job, ct);
        await instanceService.UpdateCurrentStep(instance, ct);
    }

    private async Task<EffectResult> RunJob(Job job, WorkflowInstance instance, Effect[] effects, User user,
        CancellationToken ct)
    {
        var context = modelService.CreateContext(instance);
        EffectResult result = new();

        foreach (var step in job.Steps)
        {
            var effect = effects.Single(a => a.Identifier == step.Identifier);
            if (!effect.Condition.IsMet(context)) continue;

            try
            {
                result += await effectService.RunEffect(job.Input, instance, effect, user, context, ct);
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