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
            .Where(log => log.Type != EventLogOperation.Undo &&
                          (log.OperationId == null || !undone.Contains(log.OperationId)))
            .ToArray();
    }

    public static IReadOnlyDictionary<string, InstanceEventLogEntry> LatestOperations(
        IEnumerable<InstanceEventLogEntry> eventLogs)
        => Project(eventLogs)
            .Where(log => log.OperationMetadata != null)
            .GroupBy(log => log.OperationMetadata!.TopLevelStep)
            .ToDictionary(
                group => group.Key,
                group => group.Last());

    private static HashSet<string> UndoneOperationIds(IEnumerable<InstanceEventLogEntry> eventLogs)
        => eventLogs
            .Where(log => log.Type == EventLogOperation.Undo)
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
            if (log.Type == EventLogOperation.Delete)
                events.Remove(log.EventId);
            else
                events[log.EventId] = new InstanceEvent { Id = log.EventId, Date = log.EventDate };
        }

        return events;
    }
}