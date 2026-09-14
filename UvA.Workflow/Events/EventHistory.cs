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
        IEnumerable<InstanceEventLogEntry> eventLogs)
    {
        var undone = UndoneOperationIds(eventLogs);

        return eventLogs
            .Where(log => log.OperationMetadata != null && !undone.Contains(log.OperationMetadata.Id))
            .GroupBy(log => log.OperationMetadata!.TopLevelStep)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(log => log.Timestamp)
                    .ThenByDescending(log => log.OperationMetadata!.Id, StringComparer.Ordinal)
                    .First());
    }

    public static InstanceEventLogEntry? FindOperation(
        IEnumerable<InstanceEventLogEntry> eventLogs,
        string operationId)
        => eventLogs.FirstOrDefault(log => log.OperationMetadata?.Id == operationId);

    private static HashSet<string> UndoneOperationIds(IEnumerable<InstanceEventLogEntry> eventLogs)
        => eventLogs
            .Where(log => log.Operation == EventLogOperation.Undo)
            .Select(log => log.TargetOperationId)
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