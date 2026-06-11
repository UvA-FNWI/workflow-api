using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Jobs;
using UvA.Workflow.Journaling;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Submissions;

public record SubmissionContext(
    WorkflowInstance Instance,
    FormSubmissionState SubmissionState,
    Form Form,
    string SubmissionId);

public record SubmissionResult(
    bool Success,
    InvalidQuestion[] Errors,
    FormSubmissionState SubmissionState,
    EffectResult? EffectResult = null);

public record InvalidQuestion(
    string QuestionName,
    BilingualString ValidationMessage);

public class SubmissionService(
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    InstanceService instanceService,
    IInstanceJournalService instanceJournalService,
    WorkflowInstanceService workflowInstanceService,
    JobService jobService,
    EffectService effectService
)
{
    public async Task<SubmissionContext> GetSubmissionContext(string instanceId, string submissionId,
        int? version = null,
        CancellationToken ct = default)
    {
        // Get the workflow instance
        var instance = version is null
            ? await workflowInstanceRepository.GetById(instanceId, ct)
            : await workflowInstanceService.GetAsOfVersion(instanceId, version.Value, ct);
        if (instance == null)
            throw new EntityNotFoundException("WorkflowInstance", instanceId);

        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        var form = modelService.GetForm(instance, submissionId);
        if (form == null)
            throw new EntityNotFoundException("Form", $"instanceId:{instanceId},submission:{submissionId}");

        var submissionState = FormSubmissionState.Resolve(instance, form, workflowDef);

        return new SubmissionContext(instance, submissionState, form, submissionId);
    }

    public async Task<SubmissionResult> SubmitSubmission(SubmissionContext context, User user, CancellationToken ct)
    {
        var (instance, submissionState, form, submissionId) = context;
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        // Check if already submitted
        if (submissionState.IsSubmitted)
            throw new InvalidWorkflowStateException(instance.Id, "SubmissionsAlreadySubmitted",
                "Submission already submitted");

        var objectContext = modelService.CreateContext(instance);

        // Validate required fields
        var missing = form.PropertyDefinitions
            .Where(q => q.IsRequired && !instance.HasAnswer(q.Name)
                                     && q.DataType != DataType.Boolean // Boolean datatype always defaults to false
                                     && q.Condition.IsMet(objectContext))
            .Select(q => new InvalidQuestion(q.Name, new BilingualString("Required field", "Verplicht veld")))
            .ToArray();

        // Validate field validation rules
        var invalid = form.PropertyDefinitions
            .Where(q => instance.HasAnswer(q.Name) && !q.Validation.IsMet(objectContext))
            .Select(q => new InvalidQuestion(
                q.Name,
                q.Validation!.Message ?? new BilingualString("Invalid value", "Ongeldige waarde")
            ));

        var validationErrors = missing.Concat(invalid).ToArray();

        if (validationErrors.Any())
        {
            return new SubmissionResult(false, validationErrors, submissionState);
        }

        if (form.EmitFormSubmitEvent)
            await effectService.AddEvent(instance, submissionId, user, ct);

        var result = await jobService.CreateAndRunJob(instance, JobSource.Submit,
            form.Name, form.OnSubmit, user, null, ct);

        var finalSubmissionState = FormSubmissionState.Resolve(instance, form, workflowDef);
        if (!finalSubmissionState.IsSubmitted)
        {
            throw new InvalidWorkflowStateException(instance.Id, "SubmissionStateNotResolved",
                $"Submitting form {form.Name} did not activate any submission event. " +
                $"Expected one of: {string.Join(", ", finalSubmissionState.SubmissionEventIds)}");
        }

        // Save the updated instance
        await instanceService.UpdateCurrentStep(instance, ct);
        await instanceJournalService.IncrementVersion(instance.Id, ct);
        return new SubmissionResult(true, [], finalSubmissionState, result);
    }
}