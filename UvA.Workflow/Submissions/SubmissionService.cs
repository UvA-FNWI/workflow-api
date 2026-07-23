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
    ModelService modelService,
    InstanceService instanceService,
    IInstanceJournalService instanceJournalService,
    JobService jobService,
    EffectService effectService
)
{
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