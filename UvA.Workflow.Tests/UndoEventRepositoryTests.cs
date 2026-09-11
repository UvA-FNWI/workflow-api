using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class UndoEventRepositoryTests
{
    [Fact]
    public async Task AddUndoEntry_AppendsUndoAndCancelsPendingJobs()
    {
        var database = new Mock<IMongoDatabase>();
        var collection = new Mock<IMongoCollection<InstanceEventLogEntry>>();
        var jobs = new Mock<IMongoCollection<Job>>();
        var instanceId = ObjectId.GenerateNewId().ToString();
        var operationId = ObjectId.GenerateNewId().ToString();
        FilterDefinition<Job>? cancelledJobsFilter = null;
        UpdateDefinition<Job>? cancelledJobsUpdate = null;
        var operation = new OperationMetadata
        {
            Id = operationId,
            TopLevelStep = "Subject",
            Step = "Subject",
            Source = "Start",
            Revision = 2
        };
        var rootCursor = Cursor(new InstanceEventLogEntry { OperationMetadata = operation });
        var undoCursor = Cursor();

        database.Setup(value => value.GetCollection<InstanceEventLogEntry>("eventlog", null))
            .Returns(collection.Object);
        database.Setup(value => value.GetCollection<Job>("jobs", null)).Returns(jobs.Object);
        collection.SetupSequence(value => value.FindAsync(
                It.IsAny<FilterDefinition<InstanceEventLogEntry>>(),
                It.IsAny<FindOptions<InstanceEventLogEntry, InstanceEventLogEntry>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootCursor)
            .ReturnsAsync(undoCursor);
        collection.Setup(value => value.InsertOneAsync(
                It.IsAny<InstanceEventLogEntry>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        jobs.Setup(value => value.UpdateManyAsync(
                It.IsAny<FilterDefinition<Job>>(),
                It.IsAny<UpdateDefinition<Job>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<Job>, UpdateDefinition<Job>, UpdateOptions, CancellationToken>((filter, update,
                _, _) =>
            {
                cancelledJobsFilter = filter;
                cancelledJobsUpdate = update;
            })
            .ReturnsAsync(Mock.Of<UpdateResult>());

        var repository = new InstanceEventRepository(database.Object);
        var result = await repository.AddUndoEntry(
            instanceId, "Subject", operationId, 2, UnitTestsHelpers.AdminUser, "because", default);

        Assert.True(result);
        collection.Verify(value => value.InsertOneAsync(
            It.Is<InstanceEventLogEntry>(entry =>
                entry.Operation == EventLogOperation.Undo &&
                entry.OperationId == operationId &&
                entry.UndoMetadata!.Revision == 2 &&
                entry.UndoMetadata.Reason == "because"),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var serializer = serializerRegistry.GetSerializer<Job>();
        var renderArgs = new RenderArgs<Job>(serializer, serializerRegistry);
        var filter = cancelledJobsFilter!.Render(renderArgs);
        var update = cancelledJobsUpdate!.Render(renderArgs);
        Assert.Equal(new ObjectId(instanceId), filter["InstanceId"].AsObjectId);
        Assert.Equal(new ObjectId(operationId), filter["Operation._id"].AsObjectId);
        Assert.Equal(nameof(JobStatus.Pending), filter["Status"].AsString);
        Assert.Equal(nameof(JobStatus.Cancelled), update["$set"]["Status"].AsString);
    }

    private static IAsyncCursor<InstanceEventLogEntry> Cursor(params InstanceEventLogEntry[] entries)
    {
        var cursor = new Mock<IAsyncCursor<InstanceEventLogEntry>>();
        cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.Length > 0)
            .ReturnsAsync(false);
        cursor.SetupGet(value => value.Current).Returns(entries);
        return cursor.Object;
    }
}