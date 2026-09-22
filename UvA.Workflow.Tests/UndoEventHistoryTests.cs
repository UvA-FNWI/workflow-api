using UvA.Workflow.Events;

namespace UvA.Workflow.Tests;

public class UndoEventHistoryTests
{
    [Fact]
    public void LatestOperations_ChoosesNewestOccurrence()
    {
        var operations = EventHistory.LatestOperations([
            Operation("older", "Subject", 1),
            Operation("newer", "Subject", 2)
        ], WorkflowDefinition);

        Assert.Equal("newer", operations["Subject"].Id);
        Assert.Equal(At(2), operations["Subject"].Timestamp);
    }

    [Fact]
    public void LatestOperations_UsesIdToBreakTimestampTies()
    {
        var operations = EventHistory.LatestOperations([
            Operation("first", "Subject", 1),
            Operation("second", "Subject", 1)
        ], WorkflowDefinition);

        Assert.Equal("second", operations["Subject"].Id);
    }

    [Fact]
    public void LatestOperations_ExcludesUndoneOperations()
    {
        var logs = new[]
        {
            Operation("older", "Subject", 1),
            Operation("newer", "Subject", 2),
            new InstanceEventLogEntry
            {
                Operation = EventLogOperation.Undo,
                OperationId = "newer"
            }
        };

        Assert.Equal("older", EventHistory.LatestOperations(logs, WorkflowDefinition)["Subject"].Id);
    }

    [Fact]
    public void LatestOperations_KeepsTopLevelStepHistoriesIndependent()
    {
        var operations = EventHistory.LatestOperations([
            Operation("first-older", "FirstChild", 1),
            Operation("second-newer", "SecondChild", 4),
            Operation("first-newer", "FirstChild", 3),
            Operation("second-older", "SecondChild", 2),
            Operation("removed", "Removed", 5)
        ], WorkflowDefinition);

        Assert.Equal("first-newer", operations["First"].Id);
        Assert.Equal("second-newer", operations["Second"].Id);
        Assert.Equal(2, operations.Count);
    }

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

    private static InstanceEventLogEntry Operation(string id, string step, int minute)
        => new()
        {
            Id = id,
            Timestamp = At(minute),
            OperationId = id,
            OperationMetadata = new OperationMetadata
            {
                Id = id,
                Step = step
            }
        };

    private static readonly WorkflowDefinition WorkflowDefinition = CreateWorkflowDefinition();

    private static WorkflowDefinition CreateWorkflowDefinition()
    {
        var first = new Step { Name = "First" };
        var second = new Step { Name = "Second" };
        var firstChild = new Step { Name = "FirstChild", ParentStep = first };
        var secondChild = new Step { Name = "SecondChild", ParentStep = second };
        return new WorkflowDefinition
        {
            AllSteps = [new Step { Name = "Subject" }, first, firstChild, second, secondChild]
        };
    }

    private static DateTime At(int minute) => new(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc);
}