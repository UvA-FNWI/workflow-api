using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Tests;

public class OperationCorrelationTests : ControllerTestsBase
{
    [Fact]
    public void IneligibleExecuteActionsDoNotCreateOperationMetadata()
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        var global = new Action { Name = "Global", Steps = ["Subject"] };
        definition.GlobalActions.Add(global);

        Assert.Null(OperationMetadata.CreateForAction(global, definition));
        Assert.Null(OperationMetadata.CreateForAction(
            new Action { Name = "NoSteps", Steps = [] }, definition));
        Assert.Null(OperationMetadata.CreateForAction(
            new Action { Name = "SeveralSteps", Steps = ["Subject", "Upload"] }, definition));
        Assert.Null(OperationMetadata.CreateForAction(
            new Action { Steps = ["Subject"] }, definition));
    }

    [Fact]
    public async Task Submission_CorrelatesConsequencesAndKeepsLateEventsIneffectiveAfterUndo()
    {
        var instance = new WorkflowInstanceBuilder()
            .With("Project", "Subject")
            .Build();
        var form = _modelService.WorkflowDefinitions[instance.WorkflowDefinition].Forms.Single(f => f.Name == "Start");
        form.Pages.Clear();
        form.OnSubmit =
        [
            new Effect { Event = "ImmediateConsequence" },
            new Effect { Event = "DelayedConsequence", Delay = "1m" }
        ];

        var database = new Mock<IMongoDatabase>();
        var eventLogs = new Mock<IMongoCollection<InstanceEventLogEntry>>();
        var instances = new Mock<IMongoCollection<WorkflowInstance>>();
        database.Setup(db => db.GetCollection<InstanceEventLogEntry>("eventlog", null)).Returns(eventLogs.Object);
        database.Setup(db => db.GetCollection<WorkflowInstance>("instances", null)).Returns(instances.Object);
        UpdateDefinition<WorkflowInstance>? repairedEvent = null;
        instances.Setup(collection => collection.FindOneAndUpdateAsync(
                It.IsAny<FilterDefinition<WorkflowInstance>>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<FindOneAndUpdateOptions<WorkflowInstance, WorkflowInstance>>(), _ct))
            .ReturnsAsync((WorkflowInstance)null!);
        instances.Setup(collection => collection.UpdateOneAsync(
                It.IsAny<FilterDefinition<WorkflowInstance>>(), It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<UpdateOptions>(), _ct))
            .Callback<FilterDefinition<WorkflowInstance>, UpdateDefinition<WorkflowInstance>, UpdateOptions,
                CancellationToken>((_, update, _, _) => repairedEvent = update)
            .ReturnsAsync(Mock.Of<UpdateResult>());

        var inserted = new List<InstanceEventLogEntry>();
        eventLogs.Setup(collection => collection.FindAsync(
                It.IsAny<FilterDefinition<InstanceEventLogEntry>>(),
                It.IsAny<FindOptions<InstanceEventLogEntry, InstanceEventLogEntry>>(), _ct))
            .ReturnsAsync(() => Cursor(inserted.Where(entry => entry.OperationMetadata != null).Take(1)));
        eventLogs.Setup(collection => collection.InsertOneAsync(
                It.IsAny<InstanceEventLogEntry>(), It.IsAny<InsertOneOptions>(), _ct))
            .Callback<InstanceEventLogEntry, InsertOneOptions, CancellationToken>((entry, _, _) => inserted.Add(entry))
            .Returns(Task.CompletedTask);

        var jobs = new List<Job>();
        _jobRepositoryMock.Setup(repository => repository.Add(It.IsAny<Job>(), _ct))
            .Callback<Job, CancellationToken>((job, _) => jobs.Add(job))
            .Returns(Task.CompletedTask);

        var eventRepository = new InstanceEventRepository(database.Object);
        var eventService = new InstanceEventService(eventRepository, _instanceJournalServiceMock.Object,
            _instanceService);
        var effectService = new EffectService(_instanceService, eventService, _modelService, _mailServiceMock.Object,
            _eduIdUserServiceMock.Object, _artifactServiceMock.Object, _mailLogRepositoryMock.Object,
            _configurationMock.Object, _loggerFactory.CreateLogger<EffectService>());
        var jobService = new JobService(effectService, _modelService, _jobRepositoryMock.Object,
            _workflowInstanceRepoMock.Object, _userRepoMock.Object, _loggerFactory.CreateLogger<JobService>(),
            _instanceService, Options.Create(new WorkerOptions { WorkerGroup = "test" }));
        var service = new SubmissionService(_modelService, _instanceService,
            _instanceJournalServiceMock.Object, jobService, effectService);
        var context = new SubmissionContext(instance, FormSubmissionState.Resolve(instance, form,
            _modelService.WorkflowDefinitions[instance.WorkflowDefinition]), form, form.Name);

        await service.SubmitSubmission(context, UnitTestsHelpers.AdminUser, _ct);

        var rootEntry = inserted.Single(entry => entry.OperationMetadata != null);
        var root = rootEntry.OperationMetadata!;
        var consequence = inserted.Single(entry => entry.EventId == "ImmediateConsequence");
        Assert.Equal(root.Id, rootEntry.Id);
        Assert.Equal(root.Id, rootEntry.OperationId);
        Assert.Equal(OperationType.FormSubmission, root.Type);
        Assert.Equal("Start", root.Source);
        Assert.Equal("Start", root.Step);
        Assert.Equal("Subject", root.TopLevelStep);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, rootEntry.ExecutedBy);
        Assert.Equal(root.Id, consequence.OperationId);
        Assert.Null(consequence.OperationMetadata);
        var delayedJob = Assert.Single(jobs);
        Assert.Equal(root.Id, delayedJob.OperationId);

        inserted.Add(new InstanceEventLogEntry
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Type = EventLogOperation.Undo,
            OperationId = root.Id
        });
        instance.Events = EventHistory.RebuildEvents(inserted);
        eventLogs.Setup(collection => collection.CountDocumentsAsync(
                It.IsAny<FilterDefinition<InstanceEventLogEntry>>(),
                It.IsAny<CountOptions>(), _ct))
            .ReturnsAsync(1);
        eventLogs.Setup(collection => collection.FindAsync(
                It.IsAny<FilterDefinition<InstanceEventLogEntry>>(),
                It.IsAny<FindOptions<InstanceEventLogEntry, InstanceEventLogEntry>>(), _ct))
            .ReturnsAsync(() => Cursor(inserted));
        _workflowInstanceRepoMock.Setup(repository => repository.GetById(instance.Id, _ct))
            .ReturnsAsync(instance);
        _userRepoMock.Setup(repository => repository.GetById(UnitTestsHelpers.AdminUser.Id, _ct))
            .ReturnsAsync(UnitTestsHelpers.AdminUser);
        _jobRepositoryMock.Setup(repository => repository.Update(delayedJob, _ct))
            .Returns(Task.CompletedTask);

        await jobService.RunJob(delayedJob, _ct);

        Assert.Empty(instance.Events);
        var registry = BsonSerializer.SerializerRegistry;
        var update = repairedEvent!.Render(new RenderArgs<WorkflowInstance>(
            registry.GetSerializer<WorkflowInstance>(), registry));
        Assert.True(update["$unset"].AsBsonDocument.Contains("Events.DelayedConsequence"));
        Assert.False(update["$unset"].AsBsonDocument.Contains("Events"));
    }

    private static IAsyncCursor<InstanceEventLogEntry> Cursor(IEnumerable<InstanceEventLogEntry> entries)
    {
        var cursor = new Mock<IAsyncCursor<InstanceEventLogEntry>>();
        cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        cursor.SetupGet(value => value.Current).Returns(entries);
        return cursor.Object;
    }
}