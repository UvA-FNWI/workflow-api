using UvA.Workflow.Events;
using UvA.Workflow.Journaling;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Submissions;

public record AnswerChange(BsonValue? Value, DateTime ChangedAt, string? ChangedBy);

public record AnswerChangeGroup(int VersionNumber, AnswerChange[] Changes);

/// <summary>
/// Rebuilds an answer's history for each time its form was submitted.
///
/// The property journal records the value before a write. The event log tells us
/// which form submission, if any, was still active when that write happened. We
/// need both histories to attach an edit to the right submission.
///
/// If the answer changed after an active submission, the result contains every
/// form submission. Groups and changes are newest first. Each group ends with
/// the value that existed when that submission was made.
/// </summary>
public static class AnswerChangeHistory
{
    public static AnswerChangeGroup[] For(
        IEnumerable<PropertyChangeEntry> journal,
        string questionName,
        string? formPropertyName,
        IEnumerable<string> submissionEventIds,
        IEnumerable<InstanceEventLogEntry> eventLogs,
        WorkflowDefinition workflowDefinition,
        BsonValue? currentValue)
    {
        var nestedPath = formPropertyName == null ? null : $"{formPropertyName}.{questionName}";
        var edits = journal
            .Where(change => change.Path == questionName || change.Path == nestedPath)
            .OrderBy(change => change.Timestamp)
            .ToArray();
        var logs = eventLogs.OrderBy(log => log.Timestamp).ToArray();
        var submitIds = submissionEventIds.ToHashSet();
        var submits = logs
            .Where(log => submitIds.Contains(log.EventId) &&
                          log.Operation is EventLogOperation.Create or EventLogOperation.Update)
            .Select(log => log.EventDate ?? log.Timestamp)
            .ToArray();

        var postSubmitEdits = new List<(int VersionNumber, AnswerChange Change)>();

        // An edit belongs to a submission only while that submission is active.
        // Replaying the events at the edit time lets the normal suppression rules
        // exclude edits made before submission or after rejection.
        for (var i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            var submittedAt = FormSubmissionState.Resolve(
                new WorkflowInstance { Events = EventsAt(logs, edit.Timestamp) },
                submitIds,
                workflowDefinition).DateSubmitted;
            var submitIndex = submittedAt == null ? -1 : Array.IndexOf(submits, submittedAt.Value);
            if (submitIndex < 0 || edit.Timestamp <= submittedAt)
                continue;

            // OldValue is the value before this edit. The value after it is the
            // next edit's OldValue, or the current value for the final edit.
            var value = i + 1 < edits.Length ? edits[i + 1].OldValue : currentValue;
            postSubmitEdits.Add((submitIndex + 1, new AnswerChange(value, edit.Timestamp, edit.ModifiedBy)));
        }

        if (postSubmitEdits.Count == 0)
            return [];

        // Once post-submit history exists, include every submission. Each group
        // starts with its newest edits and ends with its submitted value.
        return submits
            .Select((submittedAt, index) => new AnswerChangeGroup(
                index + 1,
                postSubmitEdits
                    .Where(edit => edit.VersionNumber == index + 1)
                    .Select(edit => edit.Change)
                    .Reverse()
                    .Append(new AnswerChange(ValueAt(edits, submittedAt, currentValue), submittedAt, null))
                    .ToArray()))
            .Reverse()
            .ToArray();
    }

    // The first edit at or after this time holds the value that existed then.
    private static BsonValue? ValueAt(PropertyChangeEntry[] edits, DateTime at, BsonValue? currentValue)
    {
        foreach (var edit in edits)
            if (edit.Timestamp >= at)
                return edit.OldValue;

        return currentValue;
    }

    private static Dictionary<string, InstanceEvent> EventsAt(InstanceEventLogEntry[] logs, DateTime at)
    {
        // Rebuild the instance's events at this point in time. FormSubmissionState
        // then applies the same suppression rules used for the live instance.
        var events = new Dictionary<string, InstanceEvent>();
        foreach (var log in logs)
        {
            if ((log.EventDate ?? log.Timestamp) > at)
                break;

            if (log.Operation == EventLogOperation.Delete)
                events.Remove(log.EventId);
            else
                events[log.EventId] = new InstanceEvent { Id = log.EventId, Date = log.EventDate };
        }

        return events;
    }
}