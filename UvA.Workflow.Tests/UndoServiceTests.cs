using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowModel;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests;

public class UndoServiceTests : ControllerTestsBase
{
    [Fact]
    public async Task Undo_RemovesLatestAuthorizedOperationAndPersistsRefreshedEvents()
    {
        var instance = new WorkflowInstanceBuilder()
            .With("Project", "Subject")
            .WithEvent("Start")
            .WithEvent("ImmediateConsequence")
            .Build();
        var operationId = "first-operation";
        var operation = new OperationMetadata
        {
            Id = operationId,
            Type = OperationType.FormSubmission,
            Source = "Start",
            Step = "Subject",
            TopLevelStep = "Subject",
            Revision = 1,
            ExecutedBy = UnitTestsHelpers.AdminUser.Id
        };
        var logs = new List<InstanceEventLogEntry>
        {
            EventLog("Start", operationId, operation),
            EventLog("ImmediateConsequence", operationId)
        };
        _eventRepoMock.Setup(r => r.GetEventLogEntriesForInstance(instance.Id, _ct))
            .ReturnsAsync(() => logs.ToList());
        _eventRepoMock.Setup(r => r.AddUndoEntry(
                instance.Id, "Subject", operationId, 1, UnitTestsHelpers.AdminUser, "because", _ct))
            .Callback(() => logs.Add(new InstanceEventLogEntry
            {
                Id = "undo-entry",
                WorkflowInstanceId = instance.Id,
                Operation = EventLogOperation.Undo,
                OperationId = operationId,
                UndoMetadata = new UndoMetadata
                {
                    TargetOperationId = operationId,
                    TopLevelStep = "Subject",
                    Revision = 1,
                    Reason = "because"
                }
            }))
            .ReturnsAsync(true);
        _modelParser.Roles.Add(new Role
        {
            Name = "Undoer",
            Actions = [new DomainAction { Type = RoleAction.Undo, Steps = ["Subject"], Form = "Start" }]
        });
        MockCurrentUser("Undoer");

        var service = new UndoService(_eventRepoMock.Object, _workflowInstanceRepoMock.Object,
            _instanceService, _rightsService);

        await service.Undo(instance, operationId, "  because  ", UnitTestsHelpers.AdminUser, _ct);

        Assert.DoesNotContain("Start", instance.Events);
        Assert.DoesNotContain("ImmediateConsequence", instance.Events);
        _eventRepoMock.Verify(r => r.AddUndoEntry(
            instance.Id, "Subject", operationId, 1, UnitTestsHelpers.AdminUser, "because", _ct), Times.Once);
        _workflowInstanceRepoMock.Verify(r => r.Update(instance, _ct), Times.Once);
    }

    private static InstanceEventLogEntry EventLog(
        string eventId, string operationId, OperationMetadata? operation = null)
        => new()
        {
            Id = $"{eventId}-log",
            EventId = eventId,
            OperationId = operationId,
            OperationMetadata = operation,
            Operation = EventLogOperation.Create,
            Timestamp = DateTime.UtcNow,
            EventDate = DateTime.UtcNow
        };
}