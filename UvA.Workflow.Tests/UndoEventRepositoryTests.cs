using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class UndoEventRepositoryTests
{
    [Fact]
    public void EventLogEntry_IgnoresRemovedStoredFields()
    {
        var operationId = ObjectId.GenerateNewId().ToString();
        var entry = BsonSerializer.Deserialize<InstanceEventLogEntry>(new BsonDocument
        {
            ["HistoryTopLevelStep"] = "Subject",
            ["HistoryRevision"] = 3,
            ["OperationMetadata"] = new BsonDocument
            {
                ["_id"] = operationId,
                ["Revision"] = 2
            },
            ["UndoMetadata"] = new BsonDocument
            {
                ["TargetOperationId"] = operationId,
                ["TopLevelStep"] = "Subject",
                ["Revision"] = 2,
                ["Reason"] = "because"
            }
        });

        Assert.Equal(operationId, entry.OperationMetadata!.Id);
        Assert.Null(entry.TargetOperationId);
        Assert.Null(entry.Reason);
    }

    [Fact]
    public async Task AddUndoEntry_AppendsUndo()
    {
        var database = new Mock<IMongoDatabase>();
        var collection = new Mock<IMongoCollection<InstanceEventLogEntry>>();
        var instanceId = ObjectId.GenerateNewId().ToString();
        var operationId = ObjectId.GenerateNewId().ToString();
        InstanceEventLogEntry? inserted = null;

        database.Setup(value => value.GetCollection<InstanceEventLogEntry>("eventlog", null))
            .Returns(collection.Object);
        collection.Setup(value => value.InsertOneAsync(
                It.IsAny<InstanceEventLogEntry>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<InstanceEventLogEntry, InsertOneOptions, CancellationToken>((entry, _, _) => inserted = entry)
            .Returns(Task.CompletedTask);
        var repository = new InstanceEventRepository(database.Object);
        var before = DateTime.UtcNow;

        await repository.AddUndoEntry(
            instanceId, operationId, UnitTestsHelpers.AdminUser, "because", default);

        Assert.NotNull(inserted);
        Assert.Equal(EventLogOperation.Undo, inserted.Operation);
        Assert.Null(inserted.EventId);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, inserted.ExecutedBy);
        Assert.InRange(inserted.Timestamp, before, DateTime.UtcNow);
        Assert.Null(inserted.OperationId);
        Assert.Equal(operationId, inserted.TargetOperationId);
        Assert.Equal("because", inserted.Reason);
        collection.Verify(value => value.InsertOneAsync(
                It.IsAny<InstanceEventLogEntry>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}