using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Journaling;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests.WorkflowInstances;

public class InstanceHistoryDtoFactoryTests
{
    private readonly ModelService _modelService = new(UnitTestsHelpers.CreateModelParser());
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();
    private readonly Mock<IInstanceJournalService> _journalServiceMock = new();
    private readonly Mock<IInstanceEventRepository> _eventRepositoryMock = new();
    private readonly InstanceHistoryDtoFactory _factory;

    public InstanceHistoryDtoFactoryTests()
    {
        _userRepositoryMock
            .Setup(repository => repository.GetByIds(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<User>());
        _userServiceMock
            .Setup(service => service.GetUsers(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, User>());

        var workflowInstanceService = new WorkflowInstanceService(
            _modelService,
            Mock.Of<IWorkflowInstanceRepository>(),
            _journalServiceMock.Object,
            _eventRepositoryMock.Object,
            _userServiceMock.Object,
            _userRepositoryMock.Object);
        _factory = new InstanceHistoryDtoFactory(
            _modelService,
            workflowInstanceService,
            _userServiceMock.Object,
            _userRepositoryMock.Object);
    }

    [Fact]
    public async Task Create_MapsAndOrdersEventEntriesAndUndoTitles()
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.Events.Add(new EventDefinition
        {
            Name = "KnownEvent",
            Label = new BilingualString("Known event", "Bekende gebeurtenis")
        });
        definition.Forms.Single(form => form.Name == "Start").Title =
            new BilingualString("Start form", "Startformulier");
        definition.GlobalActions.Add(new DomainAction
        {
            Name = "Archive",
            Label = new BilingualString("Archive", "Archiveren")
        });

        var instance = CreateInstance();
        var start = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var formUndo = start.AddMinutes(1);
        var action = start.AddMinutes(2);
        var actionUndo = start.AddMinutes(3);
        _userRepositoryMock
            .Setup(repository => repository.GetByIds(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new User
                {
                    Id = "known-user",
                    UserName = "known",
                    DisplayName = "Known User",
                    Email = "known@example.org"
                }
            ]);
        MockHistory(instance, null,
        [
            EventLog("form-operation", EventLogOperation.Create, start, "known-user", "KnownEvent",
                new OperationMetadata
                {
                    Id = "form-operation",
                    Type = OperationType.FormSubmission,
                    Source = "Start",
                    Step = "Subject"
                }),
            EventLog("form-undo", EventLogOperation.Undo, formUndo, "unknown-user",
                operationId: "form-operation", reason: "Submitted by mistake"),
            EventLog("action-operation", EventLogOperation.Update, action, "known-user",
                operationMetadata: new OperationMetadata
                {
                    Id = "action-operation",
                    Type = OperationType.ExecuteAction,
                    Source = "Archive",
                    Step = "Subject"
                }),
            EventLog("action-undo", EventLogOperation.Undo, actionUndo, "known-user",
                operationId: "action-operation"),
            EventLog("orphan-undo", EventLogOperation.Undo, actionUndo.AddMinutes(1), "unknown-user",
                operationId: "missing-operation"),
            EventLog("deleted", EventLogOperation.Delete, actionUndo.AddMinutes(1), "known-user", "KnownEvent")
        ]);

        var result = await _factory.Create(instance, CancellationToken.None);

        Assert.Collection(result,
            item =>
            {
                Assert.Equal("orphan-undo", item.Id);
                Assert.Equal(InstanceHistoryType.Undo, item.Type);
                Assert.Equal("missing-operation reversed", item.Title.En);
            },
            item =>
            {
                Assert.Equal("action-undo", item.Id);
                Assert.Equal(InstanceHistoryType.Undo, item.Type);
                Assert.Equal("Archive reversed", item.Title.En);
                Assert.Equal("Archiveren teruggedraaid", item.Title.Nl);
                Assert.Equal("known", item.User.UserName);
                Assert.Null(item.UndoHistory?.Reason);
            },
            item =>
            {
                Assert.Equal("action-operation", item.Id);
                Assert.Equal(InstanceHistoryType.Event, item.Type);
                Assert.Equal("UnknownEvent", item.Title.En);
            },
            item =>
            {
                Assert.Equal("form-undo", item.Id);
                Assert.Equal(InstanceHistoryType.Undo, item.Type);
                Assert.Equal("Start form reversed", item.Title.En);
                Assert.Equal("Startformulier teruggedraaid", item.Title.Nl);
                Assert.Equal("unknown-user", item.User.UserName);
                Assert.Equal("Submitted by mistake", item.UndoHistory?.Reason);
            },
            item =>
            {
                Assert.Equal("form-operation", item.Id);
                Assert.Equal(InstanceHistoryType.Event, item.Type);
                Assert.Equal("Known event", item.Title.En);
                Assert.Equal("Bekende gebeurtenis", item.Title.Nl);
            });
    }

    [Fact]
    public async Task Create_OrdersEventAndPropertyEntriesTogether()
    {
        var instance = CreateInstance(12);
        var submittedAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var changedAt = submittedAt.AddMinutes(1);
        var updatedAt = submittedAt.AddMinutes(2);
        MockHistory(instance,
            new InstanceJournalEntry
            {
                PropertyChanges = [Change("EC", 6, "editor", changedAt)]
            },
            [
                EventLog("submitted", EventLogOperation.Create, submittedAt, eventId: "Start"),
                EventLog("updated", EventLogOperation.Update, updatedAt, eventId: "UnknownEvent")
            ]);

        var result = await _factory.Create(instance, CancellationToken.None);

        Assert.Equal(["updated", $"EC:{changedAt.Ticks}", "submitted"], result.Select(item => item.Id));
    }

    [Fact]
    public async Task Create_MapsJournalPropertyEditAndChangedByDisplayName()
    {
        var instance = CreateInstance(12);
        var submittedAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var changedAt = submittedAt.AddMinutes(1);
        var change = Change("EC", 6, "editor", changedAt);
        _userServiceMock
            .Setup(service => service.GetUsers(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, User>
            {
                ["editor"] = new()
                {
                    UserName = "editor",
                    DisplayName = "Ada Editor",
                    Email = "ada@example.org"
                }
            });
        MockHistory(instance,
            new InstanceJournalEntry { PropertyChanges = [change] },
            [EventLog("submission", EventLogOperation.Create, submittedAt, eventId: "Start")]);

        var result = await _factory.Create(instance, CancellationToken.None);

        var item = Assert.Single(result, entry => entry.Type == InstanceHistoryType.Property);
        Assert.Equal($"EC:{changedAt.Ticks}", item.Id);
        Assert.Equal(InstanceHistoryType.Property, item.Type);
        Assert.Equal("EC changed", item.Title.En);
        Assert.Equal("EC aangepast", item.Title.Nl);
        Assert.Equal(changedAt, item.Timestamp);
        Assert.Equal("Ada Editor", item.User.DisplayName);
        Assert.NotNull(item.PropertyHistory);
        Assert.Equal("EC", item.PropertyHistory.Question.Name);
        var changes = item.PropertyHistory.AnswerChangeGroup.Changes;
        Assert.Collection(changes,
            current =>
            {
                Assert.Equal(12, current.Value?.GetInt32());
                Assert.Equal(changedAt, current.ChangedAt);
                Assert.Equal("Ada Editor", current.ChangedBy);
            },
            submitted =>
            {
                Assert.Equal(6, submitted.Value?.GetInt32());
                Assert.Equal(submittedAt, submitted.ChangedAt);
                Assert.Null(submitted.ChangedBy);
            });
    }

    [Fact]
    public async Task Create_DeduplicatesEditsSharedByForms()
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        var startForm = definition.Forms.Single(form => form.Name == "Start");
        var derivedForm = startForm.Clone();
        derivedForm.Name = "StartAlias";
        derivedForm.SubmittedWhenEvents = ["Start"];
        definition.Forms.Add(derivedForm);

        var instance = CreateInstance(12);
        var submittedAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var changedAt = submittedAt.AddMinutes(1);
        MockHistory(instance,
            new InstanceJournalEntry
            {
                PropertyChanges = [Change("EC", 6, "editor", changedAt)]
            },
            [EventLog("submission", EventLogOperation.Create, submittedAt, eventId: "Start")]);

        var result = await _factory.Create(instance, CancellationToken.None);

        Assert.Single(result, item => item.Type == InstanceHistoryType.Property);
    }

    [Fact]
    public async Task Create_ReturnsEmptyWhenHistoryHasNoDisplayableEntries()
    {
        var instance = CreateInstance();
        MockHistory(instance, null,
        [
            EventLog("deleted", EventLogOperation.Delete,
                new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc), eventId: "Start")
        ]);

        var result = await _factory.Create(instance, CancellationToken.None);

        Assert.Empty(result);
    }

    private static WorkflowInstance CreateInstance(int? ec = null)
    {
        var builder = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Subject");
        if (ec.HasValue)
            builder.WithProperties(("EC", _ => (BsonValue)ec.Value));
        return builder.Build();
    }

    private static PropertyChangeEntry Change(string path, BsonValue oldValue, string changedBy, DateTime timestamp)
        => BsonSerializer.Deserialize<PropertyChangeEntry>(new BsonDocument
        {
            [nameof(PropertyChangeEntry.Timestamp)] = timestamp,
            [nameof(PropertyChangeEntry.Path)] = path,
            [nameof(PropertyChangeEntry.OldValue)] = oldValue,
            [nameof(PropertyChangeEntry.ModifiedBy)] = changedBy
        });

    private static InstanceEventLogEntry EventLog(
        string id,
        EventLogOperation operation,
        DateTime timestamp,
        string executedBy = "unknown-user",
        string eventId = "UnknownEvent",
        OperationMetadata? operationMetadata = null,
        string? operationId = null,
        string? reason = null)
        => new()
        {
            Id = id,
            WorkflowInstanceId = "instance-id",
            EventId = eventId,
            ExecutedBy = executedBy,
            EventDate = timestamp,
            Timestamp = timestamp,
            Operation = operation,
            OperationMetadata = operationMetadata,
            OperationId = operationId,
            Reason = reason
        };

    private void MockHistory(
        WorkflowInstance instance,
        InstanceJournalEntry? journal,
        List<InstanceEventLogEntry> eventLogs)
    {
        _journalServiceMock
            .Setup(service => service.GetInstanceJournal(
                instance.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(journal);
        _eventRepositoryMock
            .Setup(repository => repository.GetEventLogEntriesForInstance(
                instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(eventLogs);
    }
}