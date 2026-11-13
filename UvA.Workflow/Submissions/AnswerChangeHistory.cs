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
/// the value that existed when that submission was made. If no answer change
/// occurred after a submission, the result is empty, even when edit history exists.
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
        BsonValue? currentValue,
        bool includeUndoneSubmissions = false)
    {
        var nestedPath = formPropertyName == null ? null : $"{formPropertyName}.{questionName}";
        var edits = journal
            .Where(change => change.Path == questionName || change.Path == nestedPath)
            .OrderBy(change => change.Timestamp)
            .ToArray();
        var logs = eventLogs.ToArray();
        var activeLogs = EventHistory.Project(logs);
        var effectiveLogs = includeUndoneSubmissions
            ? logs.OrderBy(log => log.Timestamp).ThenBy(log => log.Id).ToArray()
            : activeLogs;
        var submitIds = submissionEventIds.ToHashSet();
        var submitLogs = effectiveLogs
            .Where(log => submitIds.Contains(log.EventId) &&
                          log.Operation is EventLogOperation.Create or EventLogOperation.Update)
            .ToArray();
        var submits = submitLogs.Select(log => log.EventDate ?? log.Timestamp).ToArray();
        // Undone submissions don't count, so they take the number of the next active submission.
        var versionNumbers = submitLogs
            .Select((_, index) => submitLogs.Take(index).Count(activeLogs.Contains) + 1)
            .ToArray();

        var postSubmitEdits = new List<(int VersionNumber, AnswerChange Change)>();

        // An edit belongs to a submission only while that submission is active.
        // Replaying the events at the edit time lets the normal suppression rules
        // exclude edits made before submission or after rejection.
        for (var i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            var submittedAt = FormSubmissionState.Resolve(
                new WorkflowInstance { Events = EventHistory.RebuildEvents(logs, edit.Timestamp) },
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
                versionNumbers[index],
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
}