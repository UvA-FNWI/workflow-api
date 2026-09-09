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
        var undone = logs
            .Where(log => log.Operation == EventLogOperation.Undo)
            .Select(log => log.UndoMetadata?.TargetOperationId ?? log.OperationId)
            .Where(id => id != null)
            .ToHashSet();

        return logs
            .Where(log => log.Operation != EventLogOperation.Undo &&
                          (log.OperationId == null || !undone.Contains(log.OperationId)))
            .ToArray();
    }

    public static IReadOnlyDictionary<string, OperationMetadata> LatestOperations(
        IEnumerable<InstanceEventLogEntry> eventLogs)
    {
        var undone = eventLogs
            .Where(log => log.Operation == EventLogOperation.Undo)
            .Select(log => log.UndoMetadata?.TargetOperationId ?? log.OperationId)
            .Where(id => id != null)
            .ToHashSet();

        return eventLogs
            .Where(log => log.OperationMetadata != null && !undone.Contains(log.OperationMetadata.Id))
            .Select(log => log.OperationMetadata!)
            .GroupBy(operation => operation.TopLevelStep)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(operation => operation.Revision ?? 0).First());
    }

    public static OperationMetadata? FindOperation(
        IEnumerable<InstanceEventLogEntry> eventLogs,
        string operationId)
        => eventLogs
            .Where(log => log.OperationMetadata?.Id == operationId)
            .Select(log => log.OperationMetadata)
            .FirstOrDefault();

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