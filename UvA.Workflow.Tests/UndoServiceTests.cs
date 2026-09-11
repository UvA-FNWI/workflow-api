using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests;

public class UndoServiceTests : ControllerTestsBase
{
    [Fact]
    public async Task Undo_ExecuteActionUsesLatestOccurrenceNameAuthorizationAndInvalidatesCorrelatedEvents()
    {
        var instance = new WorkflowInstanceBuilder()
            .With("Project", "Subject")
            .WithEvent("RunAction", At(3))
            .WithEvent("Consequence", At(4))
            .Build();
        var firstOccurrence = new OperationMetadata
        {
            Id = "first-action",
            Type = OperationType.ExecuteAction,
            Source = "RunAction",
            Step = "Subject",
            TopLevelStep = "Subject",
            OccurredAt = At(1),
            Revision = 1,
            ExecutedBy = "user"
        };
        var target = firstOccurrence with { Id = "latest-action", OccurredAt = At(3), Revision = 2 };
        var logs = new List<InstanceEventLogEntry>
        {
            EventLog("RunAction", firstOccurrence.Id, firstOccurrence, At(1)),
            EventLog("RunAction", target.Id, target, At(3), EventLogOperation.Update),
            EventLog("Consequence", target.Id, at: At(4))
        };
        _eventRepoMock.Setup(r => r.GetEventLogEntriesForInstance(instance.Id, _ct))
            .ReturnsAsync(() => logs.ToList());
        _eventRepoMock.Setup(r => r.AddUndoEntry(
                instance.Id, "Subject", target.Id, 2, UnitTestsHelpers.AdminUser, "because", _ct))
            .Callback(() => logs.Add(new InstanceEventLogEntry
            {
                Id = "undo-entry",
                Timestamp = At(5),
                Operation = EventLogOperation.Undo,
                OperationId = target.Id,
                UndoMetadata = new UndoMetadata
                {
                    TargetOperationId = target.Id,
                    TopLevelStep = "Subject",
                    Revision = 2,
                    Reason = "because"
                }
            }))
            .ReturnsAsync(true);
        _modelParser.Roles.Add(new Role
        {
            Name = "WrongUndoer",
            Actions = [new DomainAction { Type = RoleAction.Undo, Steps = ["Subject"], Name = "OtherAction" }]
        });
        _modelParser.Roles.Add(new Role
        {
            Name = "Undoer",
            Actions = [new DomainAction { Type = RoleAction.Undo, Steps = ["Subject"], Name = "RunAction" }]
        });
        var service = new UndoService(_eventRepoMock.Object, _workflowInstanceRepoMock.Object,
            _instanceService, _rightsService);

        MockCurrentUser("WrongUndoer");
        Assert.Null(await service.GetCandidate(instance, "Subject", logs));

        MockCurrentUser("Undoer");
        var candidate = await service.GetCandidate(instance, "Subject", logs);
        Assert.Equal(target, candidate);

        await service.Undo(instance, target.Id, "because", UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal(At(1), instance.Events["RunAction"].Date);
        Assert.DoesNotContain("Consequence", instance.Events);
    }

    [Fact]
    public async Task Undo_PreservesLaterStepStateAndReusesItAfterResubmission()
    {
        var firstStep = new Step
        {
            Name = "First",
            Ends = new Condition
            {
                Logical = new Logical
                {
                    Operator = LogicalOperator.And,
                    Children =
                    [
                        new Condition { Event = "Restored" },
                        new Condition { Event = "FirstDone" }
                    ]
                }
            }
        };
        var secondStep = new Step { Name = "Second", Ends = new Condition { Event = "SecondDone" } };
        var thirdStep = new Step { Name = "Third" };
        var definition = new WorkflowDefinition
        {
            Name = "IndependentHistories",
            Steps = [firstStep, secondStep, thirdStep],
            AllSteps = [firstStep, secondStep, thirdStep],
            Forms = [],
            Properties = [],
            Events =
            [
                new EventDefinition { Name = "Restored" },
                new EventDefinition { Name = "FirstDone" },
                new EventDefinition { Name = "SecondDone" }
            ]
        };
        _modelParser.WorkflowDefinitions[definition.Name] = definition;

        var firstOccurrence = Operation("first-occurrence", "First", "First", 1);
        var target = Operation("target", "First", "First", 2);
        var later = Operation("later", "Second", "Second", 1);
        var logs = new List<InstanceEventLogEntry>
        {
            EventLog("Restored", firstOccurrence.Id, firstOccurrence, At(1)),
            EventLog("Restored", target.Id, target, At(2), EventLogOperation.Update),
            EventLog("FirstDone", target.Id, at: At(3)),
            EventLog("SecondDone", later.Id, later, At(4))
        };
        var instance = new WorkflowInstanceBuilder()
            .With(definition.Name, "Third")
            .WithEvent("Restored", At(2))
            .WithEvent("FirstDone", At(3))
            .WithEvent("SecondDone", At(4))
            .Build();
        _eventRepoMock.Setup(r => r.GetEventLogEntriesForInstance(instance.Id, _ct))
            .ReturnsAsync(() => logs.ToList());
        _eventRepoMock.Setup(r => r.AddUndoEntry(
                instance.Id, "First", target.Id, 2, UnitTestsHelpers.AdminUser, "because", _ct))
            .Callback(() => logs.Add(new InstanceEventLogEntry
            {
                Id = "undo-entry",
                Timestamp = At(5),
                Operation = EventLogOperation.Undo,
                OperationId = target.Id,
                UndoMetadata = new UndoMetadata
                {
                    TargetOperationId = target.Id,
                    TopLevelStep = "First",
                    Revision = 2,
                    Reason = "because"
                }
            }))
            .ReturnsAsync(true);
        _modelParser.Roles.Add(new Role
        {
            Name = "Undoer",
            Actions = [new DomainAction { Type = RoleAction.Undo, Steps = ["First"], Form = "FirstForm" }]
        });
        MockCurrentUser("Undoer");
        var service = new UndoService(_eventRepoMock.Object, _workflowInstanceRepoMock.Object,
            _instanceService, _rightsService);

        await service.Undo(instance, target.Id, "because", UnitTestsHelpers.AdminUser, _ct);

        Assert.Equal("First", instance.CurrentStep);
        Assert.Equal(At(1), instance.Events["Restored"].Date);
        Assert.Equal(At(4), instance.Events["SecondDone"].Date);
        var restoredVersion = Assert.Single(new StepVersionService().GetStepVersions(instance, firstStep, logs));
        Assert.Equal(At(1), restoredVersion.SubmittedAt);

        instance.RecordEvent("Restored", At(6));
        instance.RecordEvent("FirstDone", At(7));
        await _instanceService.UpdateCurrentStep(instance, _ct);

        Assert.Equal("Third", instance.CurrentStep);
    }

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
        string eventId,
        string operationId,
        OperationMetadata? operation = null,
        DateTime? at = null,
        EventLogOperation eventLogOperation = EventLogOperation.Create)
        => new()
        {
            Id = $"{eventId}-log",
            EventId = eventId,
            OperationId = operationId,
            OperationMetadata = operation,
            Operation = eventLogOperation,
            Timestamp = at ?? DateTime.UtcNow,
            EventDate = at ?? DateTime.UtcNow
        };

    private static OperationMetadata Operation(string id, string step, string topLevelStep, long revision)
        => new()
        {
            Id = id,
            Type = OperationType.FormSubmission,
            Source = "FirstForm",
            Step = step,
            TopLevelStep = topLevelStep,
            Revision = revision,
            ExecutedBy = "user"
        };

    private static DateTime At(int minute) => new(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc);
}