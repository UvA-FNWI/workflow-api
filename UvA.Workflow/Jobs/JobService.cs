using System.Net;
using Microsoft.Extensions.Logging;
using UvA.Workflow.Notifications;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;
using Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Jobs;

public record JobInput(MailMessage? Mail);

public class JobService(
    EffectService effectService,
    ModelService modelService,
    IJobRepository repository,
    IWorkflowInstanceRepository workflowInstanceRepository,
    IUserRepository userRepository,
    ILogger<JobService> logger,
    InstanceService instanceService,
    IOptions<WorkerOptions> workerOptions)
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
            WorkerGroup = workerOptions.Value.WorkerGroup,
            Steps = steps.Keys.ToList()
        };

        var result = await RunJob(job, instance, effects, user, ct);
        var jobStepsBeforeFiltering = job.Steps;
        job.Steps = job.Steps.Where(s => steps[s].IsLogged).ToList();
        if (job.Steps.Count > 0)
            await repository.Add(job, ct);
        if (job.Status == JobStatus.Failed)
        {
            var failedSteps = jobStepsBeforeFiltering.FindAll(s => s.Message != null);
            var failedEffects = failedSteps.Count > 0
                ? failedSteps.Select(s => steps.Values.SingleOrDefault(e => e.Identifier == s.Identifier)).ToList()
                : null;

            var failedDescriptions = failedSteps
                .Select(s =>
                {
                    var effect = failedEffects?.SingleOrDefault(e => e?.Identifier == s.Identifier);
                    var kind = effect?.IsExternal == true ? "external" : "internal";
                    return $"  - [{kind}] {s.Identifier}: {s.Message}";
                });

            var summary = string.Join(Environment.NewLine, failedDescriptions);
            var jobFailedErrorMessage = job.SourceType switch
            {
                JobSource.Submit => new BilingualString(
                    "The form was submitted successfully, but a background task has failed.",
                    "Het formulier is succesvol ingeleverd, maar een achtergrondtaak is mislukt."),
                JobSource.Save => new BilingualString(
                    "The form was saved successfully, but a background task has failed.",
                    "Het formulier is succesvol opgeslagen, maar een achtergrondtaak is mislukt."),
                JobSource.Action => new BilingualString(
                    "The action was executed successfully, but a background task has failed.",
                    "De actie is succesvol uitgevoerd, maar een achtergrondtaak is mislukt."),
                _ => new BilingualString(
                    "A background task has failed.",
                    "Een achtergrondtaak is mislukt.")
            };

            if (failedEffects?.All(e => e?.IsExternal == true) == true)
            {
                logger.LogError("Job {job.Id} failed due to external service(s):{Environment.NewLine}{summary}", job.Id,
                    Environment.NewLine, summary);
                result.Error = new EffectError(jobFailedErrorMessage,
                    true, instance.Id);
                return result;
            }

            logger.LogError("Job {job.Id} failed: {Environment.NewLine}{summary}", job.Id, Environment.NewLine,
                summary);
            result.Error = new EffectError(jobFailedErrorMessage, false, instance.Id);
            return result;
        }

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
                WorkerGroup = workerOptions.Value.WorkerGroup,
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
        var user = await userRepository.GetById(job.CreatedBy!, ct);
        if (user == null)
        {
            logger.LogError("Job {Job}: user {UserId} not found", job.Id, job.CreatedBy);
            throw new Exception($"User {job.CreatedBy} not found");
        }

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
            var effect = effects.SingleOrDefault(a => a.Identifier == step.Identifier);
            if (effect == null)
            {
                logger.LogError(
                    "Job {Job}: step identifier {StepIdentifier} not found in current model effects [{AvailableIdentifiers}].",
                    job.Id,
                    step.Identifier,
                    string.Join(", ", effects.Select(e => e.Identifier)));
                step.Message = $"Effect '{step.Identifier}' not found in the workflow definition";
                job.Status = JobStatus.Failed;
                return result;
            }

            if (!effect.Condition.IsMet(context)) continue;

            try
            {
                result += await effectService.RunEffect(job, instance, effect, user, context, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running effect {Effect}", effect.Identifier);
                step.Message = step.Message = $"{ex.GetType().Name}: {ex.Message}";
                ;
                job.Status = JobStatus.Failed;
                continue;
            }

            var outputs = context.Get(effect.Name ?? effect.ServiceCall?.Operation ?? "__invalid")
                as Dictionary<Lookup, object>;
            step.Outputs = outputs?.ToDictionary(o => o.Key.ToString(), o => o.Value);
        }

        if (job.Status != JobStatus.Failed)
            job.Status = JobStatus.Completed;
        job.ExecutedOn = DateTime.Now;
        return result;
    }
}