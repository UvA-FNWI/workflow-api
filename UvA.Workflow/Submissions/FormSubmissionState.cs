using UvA.Workflow.Events;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Submissions;

public record FormSubmissionState(
    string[] SubmissionEventIds,
    InstanceEvent[] ActiveSubmissionEvents,
    DateTime? DateSubmitted)
{
    public bool IsSubmitted => ActiveSubmissionEvents.Length != 0;

    public string[] ActiveSubmissionEventIds => ActiveSubmissionEvents.Select(e => e.Id).ToArray();

    public static string[] GetSubmissionEventIds(Form form)
        => form.SubmittedWhenEvents is { Length: > 0 } ? form.SubmittedWhenEvents : [form.Name];

    public static FormSubmissionState Resolve(WorkflowInstance instance, Form form, WorkflowDefinition workflowDef)
    {
        var submissionEventIds = GetSubmissionEventIds(form);
        var activeSubmissionEvents = submissionEventIds
            .Select(eventId => instance.Events.GetValueOrDefault(eventId))
            .Where(e => e?.Date != null)
            .Cast<InstanceEvent>()
            .Where(e => EventSuppressionHelper.IsEventActive(e.Id, instance, workflowDef))
            .OrderByDescending(e => e.Date)
            .ToArray();

        return new FormSubmissionState(
            submissionEventIds,
            activeSubmissionEvents,
            activeSubmissionEvents.FirstOrDefault()?.Date);
    }
}