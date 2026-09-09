using Moq;
using MongoDB.Driver;
using UvA.Workflow.Events;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class UndoEventRepositoryTests
{
    [Fact]
    public async Task AddUndoEntry_AppendsOnlyWhenExpectedCandidateRevisionStillMatches()
    {
        var database = new Mock<IMongoDatabase>();
        var client = new Mock<IMongoClient>();
        var session = new Mock<IClientSessionHandle>();
        var collection = new Mock<IMongoCollection<InstanceEventLogEntry>>();
        var operation = new OperationMetadata
        {
            Id = "operation",
            TopLevelStep = "Subject",
            Step = "Subject",
            Source = "Start",
            Revision = 2
        };
        var rootCursor = Cursor(new InstanceEventLogEntry { OperationMetadata = operation });
        var undoCursor = Cursor();

        database.SetupGet(value => value.Client).Returns(client.Object);
        database.Setup(value => value.GetCollection<InstanceEventLogEntry>("eventlog", null))
            .Returns(collection.Object);
        client.Setup(value => value.StartSessionAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session.Object);
        session.Setup(value => value.IsInTransaction).Returns(true);
        collection.SetupSequence(value => value.FindAsync(
                session.Object,
                It.IsAny<FilterDefinition<InstanceEventLogEntry>>(),
                It.IsAny<FindOptions<InstanceEventLogEntry, InstanceEventLogEntry>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootCursor)
            .ReturnsAsync(undoCursor);
        collection.Setup(value => value.InsertOneAsync(
                session.Object,
                It.IsAny<InstanceEventLogEntry>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var repository = new InstanceEventRepository(database.Object);
        var result = await repository.AddUndoEntry(
            "instance", "Subject", "operation", 2, UnitTestsHelpers.AdminUser, "because", default);

        Assert.True(result);
        collection.Verify(value => value.InsertOneAsync(
            session.Object,
            It.Is<InstanceEventLogEntry>(entry =>
                entry.Operation == EventLogOperation.Undo &&
                entry.OperationId == "operation" &&
                entry.UndoMetadata!.Revision == 2 &&
                entry.UndoMetadata.Reason == "because"),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
        session.Verify(value => value.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
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