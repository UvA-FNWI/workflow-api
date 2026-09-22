using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Events;

public static class EventHistory
{
    public static IReadOnlyList<InstanceEventLogEntry> Project(
        IEnumerable<InstanceEventLogEntry> eventLogs,
        DateTime? at = null)
    {
        var logs = eventLogs
            .Where(log => at == null || log.Timestamp <= at)
            .OrderBy(log => log.Timestamp)
            .ThenBy(log => log.Id)
            .ToArray();
        var undone = UndoneOperationIds(logs);

        return logs
            .Where(log => log.Operation != EventLogOperation.Undo &&
                          (log.OperationId == null || !undone.Contains(log.OperationId)))
            .ToArray();
    }

    public static IReadOnlyDictionary<string, InstanceEventLogEntry> LatestOperations(
        IEnumerable<InstanceEventLogEntry> eventLogs,
        WorkflowDefinition workflowDefinition)
        => Project(eventLogs)
            .Where(log => log.OperationMetadata != null)
            .Select(log => new
            {
                Log = log,
                TopLevelStep = GetTopLevelStep(workflowDefinition, log.OperationMetadata!.Step)
            })
            .Where(item => item.TopLevelStep != null)
            .GroupBy(item => item.TopLevelStep!.Name)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Log);

    private static Step? GetTopLevelStep(WorkflowDefinition workflowDefinition, string stepName)
    {
        var step = workflowDefinition.AllSteps.SingleOrDefault(candidate => candidate.Name == stepName);
        while (step?.ParentStep != null)
            step = step.ParentStep;
        return step;
    }

    private static HashSet<string> UndoneOperationIds(IEnumerable<InstanceEventLogEntry> eventLogs)
        => eventLogs
            .Where(log => log.Operation == EventLogOperation.Undo)
            .Select(log => log.OperationId)
            .OfType<string>()
            .ToHashSet();

    public static Dictionary<string, InstanceEvent> RebuildEvents(
        IEnumerable<InstanceEventLogEntry> eventLogs,
        DateTime? at = null)
    {
        var events = new Dictionary<string, InstanceEvent>();
        foreach (var log in Project(eventLogs, at))
        {
            if (log.Operation == EventLogOperation.Delete)
                events.Remove(log.EventId);
            else
                events[log.EventId] = new InstanceEvent { Id = log.EventId, Date = log.EventDate };
        }

        return events;
    }
}