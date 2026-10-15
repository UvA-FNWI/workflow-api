using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Journaling;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class DeadlinePostponementJournalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Postponements_AppendEveryEntry_AndCreateMissingJournals(bool existingJournal)
    {
        var instanceId = ObjectId.GenerateNewId().ToString();
        var journal = new InstanceJournalEntry { InstanceId = instanceId, CurrentVersion = 3 };
        var collection = new Mock<IMongoCollection<InstanceJournalEntry>>();
        var writes = new List<(BsonDocument Update, bool Upsert)>();
        collection.Setup(value => value.FindAsync(It.IsAny<FilterDefinition<InstanceJournalEntry>>(),
                It.IsAny<FindOptions<InstanceJournalEntry, InstanceJournalEntry>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(existingJournal ? [journal] : []));
        collection.Setup(value => value.UpdateOneAsync(It.IsAny<FilterDefinition<InstanceJournalEntry>>(),
                It.IsAny<UpdateDefinition<InstanceJournalEntry>>(), It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<InstanceJournalEntry>, UpdateDefinition<InstanceJournalEntry>,
                UpdateOptions, CancellationToken>((_, update, options, _) =>
                writes.Add((update.Render(Args()).AsBsonDocument, options.IsUpsert)))
            .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
        var database = new Mock<IMongoDatabase>();
        database.Setup(value => value.GetCollection<InstanceJournalEntry>("instance_journal", null))
            .Returns(collection.Object);
        var service = new InstanceJournalService(database.Object);

        foreach (var reason in new[] { "illness", "more time" })
        {
            var entry = PropertyChangeEntry.Create("DueDate", "old",
                UnitTestsHelpers.AdminUser, reason: reason);
            Assert.False(await service.LogPropertyChange(instanceId, entry));
        }

        Assert.Equal(2, writes.Count);
        Assert.All(writes, write =>
        {
            Assert.True(write.Upsert);
            Assert.False(write.Update.Contains("$set"));
            var entry = BsonSerializer.Deserialize<PropertyChangeEntry>(write.Update["$push"]["PropertyChanges"]
                .AsBsonDocument);
            Assert.Equal("old", entry.OldValue?.AsString);
            Assert.False(write.Update["$push"]["PropertyChanges"].AsBsonDocument.Contains("NewValue"));
            Assert.Equal(UnitTestsHelpers.AdminUser.UserName, entry.ModifiedBy);
            Assert.Equal(existingJournal ? 3 : 0, entry.Version);
        });
        Assert.Equal("illness", writes[0].Update["$push"]["PropertyChanges"]["Reason"].AsString);
        Assert.Equal("more time", writes[1].Update["$push"]["PropertyChanges"]["Reason"].AsString);
        journal.PropertyChanges = writes.Select(write => BsonSerializer.Deserialize<PropertyChangeEntry>(
            write.Update["$push"]["PropertyChanges"].AsBsonDocument)).ToArray();

        FilterDefinition<InstanceJournalEntry>? mergeFilter = null;
        collection.Setup(value => value.UpdateOneAsync(It.IsAny<FilterDefinition<InstanceJournalEntry>>(),
                It.IsAny<UpdateDefinition<InstanceJournalEntry>>(), It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<InstanceJournalEntry>, UpdateDefinition<InstanceJournalEntry>,
                UpdateOptions, CancellationToken>((filter, _, _, _) => mergeFilter = filter)
            .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));
        await service.LogPropertyChange(instanceId,
            PropertyChangeEntry.Create("DueDate", "old", UnitTestsHelpers.AdminUser));
        Assert.True(mergeFilter!.Render(Args())["PropertyChanges"]["$elemMatch"]["Reason"].IsBsonNull);
        if (existingJournal)
            Assert.Equal(journal.PropertyChanges.Max(entry => entry.Timestamp),
                mergeFilter.Render(Args())["PropertyChanges"]["$elemMatch"]["Timestamp"]["$gt"].ToUniversalTime());
    }

    private static RenderArgs<InstanceJournalEntry> Args() => new(
        BsonSerializer.SerializerRegistry.GetSerializer<InstanceJournalEntry>(), BsonSerializer.SerializerRegistry);

    private static IAsyncCursor<InstanceJournalEntry> Cursor(InstanceJournalEntry[] entries)
    {
        var cursor = new Mock<IAsyncCursor<InstanceJournalEntry>>();
        cursor.SetupGet(value => value.Current).Returns(entries);
        cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).ReturnsAsync(false);
        return cursor.Object;
    }
}