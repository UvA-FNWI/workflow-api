using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Events;
using UvA.Workflow.Journaling;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests;

public class InstanceJournalServiceTests
{
    [Fact]
    public async Task LogPropertyChange_MergesOnlyEditsRecordedAfterTheLatestEvent()
    {
        var instanceId = ObjectId.GenerateNewId().ToString();
        var restartedAt = new DateTime(2026, 1, 1, 0, 3, 0, DateTimeKind.Utc);
        var journal = new Mock<IMongoCollection<InstanceJournalEntry>>();
        var events = new Mock<IMongoCollection<InstanceEventLogEntry>>();
        var database = new Mock<IMongoDatabase>();
        database.Setup(db => db.GetCollection<InstanceJournalEntry>("instance_journal", null))
            .Returns(journal.Object);
        database.Setup(db => db.GetCollection<InstanceEventLogEntry>("eventlog", null))
            .Returns(events.Object);
        journal.Setup(collection => collection.FindAsync(It.IsAny<FilterDefinition<InstanceJournalEntry>>(),
                It.IsAny<FindOptions<InstanceJournalEntry, InstanceJournalEntry>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(new InstanceJournalEntry { InstanceId = instanceId, CurrentVersion = 1 }));
        FilterDefinition<InstanceEventLogEntry>? eventFilter = null;
        FindOptions<InstanceEventLogEntry, InstanceEventLogEntry>? eventOptions = null;
        events.Setup(collection => collection.FindAsync(It.IsAny<FilterDefinition<InstanceEventLogEntry>>(),
                It.IsAny<FindOptions<InstanceEventLogEntry, InstanceEventLogEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<InstanceEventLogEntry>, FindOptions<InstanceEventLogEntry, InstanceEventLogEntry>
                ,
                CancellationToken>((filter, options, _) =>
            {
                eventFilter = filter;
                eventOptions = options;
            })
            .ReturnsAsync(() => Cursor(new InstanceEventLogEntry { Timestamp = restartedAt }));
        FilterDefinition<InstanceJournalEntry>? mergeFilter = null;
        journal.Setup(collection => collection.UpdateOneAsync(It.IsAny<FilterDefinition<InstanceJournalEntry>>(),
                It.IsAny<UpdateDefinition<InstanceJournalEntry>>(), It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<InstanceJournalEntry>, UpdateDefinition<InstanceJournalEntry>, UpdateOptions,
                CancellationToken>((filter, _, _, _) => mergeFilter = filter)
            .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
        var service = new InstanceJournalService(database.Object);
        var change = PropertyChangeEntry.Create("Title", "Corrected", new User { UserName = "coordinator" });

        await service.LogPropertyChange(instanceId, change);

        Assert.NotNull(eventFilter);
        Assert.Equal(new BsonDocument("InstanceId", ObjectId.Parse(instanceId)), Render(eventFilter));
        Assert.Equal(new BsonDocument("Timestamp", -1), eventOptions!.Sort.Render(
            new RenderArgs<InstanceEventLogEntry>(BsonSerializer.LookupSerializer<InstanceEventLogEntry>(),
                BsonSerializer.SerializerRegistry)));
        var groupFilter = Render(mergeFilter!)["PropertyChanges"]["$elemMatch"].AsBsonDocument;
        Assert.Equal(new BsonDateTime(restartedAt), groupFilter["Timestamp"]["$gt"]);
        Assert.False(change.ToBsonDocument().Contains("FirstTimestamp"));
    }

    private static BsonDocument Render<T>(FilterDefinition<T> filter)
        => filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry));

    private static IAsyncCursor<T> Cursor<T>(T value)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        cursor.SetupGet(c => c.Current).Returns([value]);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).ReturnsAsync(false);
        return cursor.Object;
    }
}