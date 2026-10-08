using UvA.Workflow.Events;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Versioning;

public record StepVersion
{
    public int VersionNumber { get; init; }
    public List<string> EventIds { get; init; } = [];
    public DateTime SubmittedAt { get; init; }
}

public interface IStepVersionService
{
    List<StepVersion> GetStepVersions(
        WorkflowInstance instance,
        Step step,
        IEnumerable<InstanceEventLogEntry> eventLogs);
}

public class StepVersionService : IStepVersionService
{
    public List<StepVersion> GetStepVersions(
        WorkflowInstance instance,
        Step step,
        IEnumerable<InstanceEventLogEntry> eventLogs)
    {
        var (allChildEvents, completionCondition) = DetermineEventSets(step);
        // Only events declared on direct children reset this parent; nested resets belong
        // to their own submission rounds.
        var resetEventIds = step.Children
            .SelectMany(child => child.Events)
            .Where(ev => ev.ResetParentStep)
            .Select(ev => ev.Name)
            .ToHashSet();
        var allChildEventSet = allChildEvents.Concat(resetEventIds).ToHashSet();
        var effectiveEventLogs = EventHistory.Project(eventLogs);

        // Get submission events (create/update only), ordered chronologically
        var submissionEvents = effectiveEventLogs
            .Where(log => allChildEventSet.Contains(log.EventId))
            .Where(log => log.Operation is EventLogOperation.Create or EventLogOperation.Update)
            .OrderBy(log => log.Timestamp)
            .ToList();

        return BuildVersions(step, submissionEvents, completionCondition, resetEventIds);
    }

    private static (List<string> AllEvents, Condition? CompletionCondition) DetermineEventSets(Step step)
    {
        if (step.Ends != null)
        {
            var ownEvents = step.Ends.GetAllEventIds().ToList();
            return (ownEvents, step.Ends);
        }

        if (!step.Children.Any())
            return ([], null);

        var allChildEvents = step.Children
            .SelectMany(GetStepEventIds)
            .Distinct()
            .ToList();

        return (allChildEvents, GetCompletionCondition(step));
    }

    private List<StepVersion> BuildVersions(
        Step step,
        List<InstanceEventLogEntry> submissionEvents,
        Condition? completionCondition,
        HashSet<string> resetEventIds)
    {
        if (step.Ends == null && step.Children.Any())
            return BuildMultiEventVersions(submissionEvents, completionCondition, resetEventIds);

        return BuildSingleEventVersions(submissionEvents);
    }

    private static List<StepVersion> BuildSingleEventVersions(
        List<InstanceEventLogEntry> submissionEvents)
    {
        return submissionEvents
            .Select((logEntry, index) => new StepVersion
            {
                VersionNumber = index + 1,
                EventIds = [logEntry.EventId],
                SubmittedAt = logEntry.Timestamp
            })
            .ToList();
    }

    private static List<StepVersion> BuildMultiEventVersions(
        List<InstanceEventLogEntry> submissionEvents,
        Condition? completionCondition,
        HashSet<string> resetEventIds)
    {
        var versions = new List<StepVersion>();
        var currentVersionEvents = new List<InstanceEventLogEntry>();
        var currentVersionEventIds = new HashSet<string>();

        foreach (var logEntry in submissionEvents)
        {
            var resetsParent = resetEventIds.Contains(logEntry.EventId);
            // A form can complete the last child and then emit its reset marker. Keep
            // both in the same round instead of creating an extra reset-only version.
            if (resetsParent && currentVersionEvents.Count == 0 && versions.Count > 0)
            {
                versions[^1] = versions[^1] with
                {
                    EventIds = [.. versions[^1].EventIds, logEntry.EventId],
                    SubmittedAt = logEntry.Timestamp
                };
                continue;
            }

            currentVersionEvents.Add(logEntry);
            currentVersionEventIds.Add(logEntry.EventId);

            if (!resetsParent && !completionCondition.IsMet(currentVersionEventIds))
                continue;

            versions.Add(new StepVersion
            {
                VersionNumber = versions.Count + 1,
                EventIds = currentVersionEvents.Select(log => log.EventId).ToList(),
                SubmittedAt = currentVersionEvents.Max(log => log.Timestamp)
            });
            currentVersionEvents.Clear();
            currentVersionEventIds.Clear();
        }

        return versions;
    }

    private static IEnumerable<string> GetStepEventIds(Step step)
    {
        if (step.Ends != null)
            return step.Ends.GetAllEventIds();

        return step.Children.SelectMany(GetStepEventIds);
    }

    private static Condition? GetCompletionCondition(Step step)
    {
        if (step.Ends != null)
            return step.Ends;

        if (!step.Children.Any())
            return null;

        return step.HierarchyMode == StepHierarchyMode.Sequential
            ? GetCompletionCondition(step.Children.Last())
            : new Condition
            {
                Logical = new Logical
                {
                    Operator = LogicalOperator.And,
                    Children = step.Children
                        .Select(GetCompletionCondition)
                        .Where(condition => condition != null)
                        .Cast<Condition>()
                        .ToArray()
                }
            };
    }
}