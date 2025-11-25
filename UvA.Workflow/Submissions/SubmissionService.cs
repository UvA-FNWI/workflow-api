using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Submissions;

public record SubmissionContext(WorkflowInstance Instance, InstanceEvent? Submission, Form Form, string SubmissionId);

public record SubmissionResult(bool Success, InvalidQuestion[] Errors);

public record InvalidQuestion(
    string QuestionName,
    BilingualString ValidationMessage);

public class SubmissionService(
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
    TriggerService triggerService,
    InstanceService instanceService
)
{
    public async Task<SubmissionContext> GetSubmissionContext(string instanceId, string submissionId,
        CancellationToken ct)
    {
        // Get the workflow instance
        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            throw new EntityNotFoundException("WorkflowInstance", instanceId);

        // Get the submission
        var submission = instance.Events.GetValueOrDefault(submissionId);

        var form = modelService.GetForm(instance, submissionId);
        if (form == null)
            throw new EntityNotFoundException("Form", $"instanceId:{instanceId},submission:{submissionId}");

        return new SubmissionContext(instance, submission, form, submissionId);
    }

    public async Task<SubmissionResult> SubmitSubmission(SubmissionContext context, User user, CancellationToken ct)
    {
        var (instance, submission, form, submissionId) = context;

        // Check if already submitted
        if (submission?.Date != null)
            throw new InvalidWorkflowStateException(instance.Id, "SubmissionsAlreadySubmitted",
                "Submission already submitted");

        var objectContext = modelService.CreateContext(instance);

        // Validate required fields
        var missing = form.PropertyDefinitions
            .Where(q => q.IsRequired && !instance.HasAnswer(q.Name)
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
            return new SubmissionResult(false, validationErrors);
        }

        await triggerService.RunTriggers(instance, [new Trigger { Event = submissionId }, ..form.OnSubmit], user, ct);

        // Save the updated instance
        await instanceService.UpdateCurrentStep(instance, ct);
        return new SubmissionResult(true, []);
    }
}