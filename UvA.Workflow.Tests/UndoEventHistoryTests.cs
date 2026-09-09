using UvA.Workflow.Events;

namespace UvA.Workflow.Tests;

public class UndoEventHistoryTests
{
    [Fact]
    public void Project_ExcludesUndoneOperationAtAndAfterUndoTime()
    {
        var firstOperation = "first-operation";
        var secondOperation = "second-operation";
        var logs = new[]
        {
            Event("first", 1, firstOperation),
            Event("first-consequence", 2, firstOperation),
            Event("second", 3, secondOperation),
            new InstanceEventLogEntry
            {
                Id = "undo-entry",
                Timestamp = At(4),
                Operation = EventLogOperation.Undo,
                OperationId = firstOperation
            },
            Event("after-undo", 5, secondOperation)
        };

        Assert.Contains(EventHistory.Project(logs, At(3)), log => log.EventId == "first-consequence");

        var effective = EventHistory.Project(logs, At(5));

        Assert.Equal(["second", "after-undo"], effective.Select(log => log.EventId));
    }

    private static InstanceEventLogEntry Event(string eventId, int minute, string operationId)
        => new()
        {
            Id = $"{eventId}-log",
            Timestamp = At(minute),
            EventId = eventId,
            OperationId = operationId,
            Operation = EventLogOperation.Create
        };

    private static DateTime At(int minute) => new(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc);
}