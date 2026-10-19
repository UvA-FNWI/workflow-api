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
        var resetParentEvents = step.Children
            .SelectMany(c => c.Events)
            .Where(e => e.ResetParentStep)
            .Select(e => e.Name)
            .ToList();
        var relevantEventSet = allChildEvents.Concat(resetParentEvents).ToHashSet();
        var effectiveEventLogs = EventHistory.Project(eventLogs);

        // Get submission events (create/update only), ordered chronologically
        var submissionEvents = effectiveEventLogs
            .Where(log => relevantEventSet.Contains(log.EventId))
            .Where(log => log.Operation is EventLogOperation.Create or EventLogOperation.Update)
            .OrderBy(log => log.Timestamp)
            .ToList();

        return BuildVersions(step, submissionEvents, resetParentEvents, completionCondition);
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
        List<string> resetEvents,
        Condition? completionCondition
    )
    {
        if (step.Ends == null && step.Children.Any())
            return BuildMultiEventVersions(submissionEvents, resetEvents, completionCondition);

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
        List<string> resetEvents,
        Condition? completionCondition
    )
    {
        var versions = new List<StepVersion>();
        var currentVersionEvents = new List<InstanceEventLogEntry>();
        var currentVersionEventIds = new HashSet<string>();

        foreach (var logEntry in submissionEvents)
        {
            var isReset = resetEvents.Contains(logEntry.EventId);

            if (isReset && currentVersionEvents.Count == 0)
                continue;

            currentVersionEvents.Add(logEntry);
            currentVersionEventIds.Add(logEntry.EventId);

            if (!isReset && !completionCondition.IsMet(currentVersionEventIds))
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