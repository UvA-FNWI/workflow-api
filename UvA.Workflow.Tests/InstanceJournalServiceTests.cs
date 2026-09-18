using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Journaling;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class InstanceJournalServiceTests
{
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task DatedHistoryIsAppendedWithoutOverwritingItsTimestamp(bool isDate, bool journalExists,
        bool mergeMatched)
    {
        var id = ObjectId.GenerateNewId().ToString();
        var collection = new Mock<IMongoCollection<InstanceJournalEntry>>();
        var database = new Mock<IMongoDatabase>();
        database.Setup(db =>
                db.GetCollection<InstanceJournalEntry>("instance_journal", It.IsAny<MongoCollectionSettings>()))
            .Returns(collection.Object);
        var cursor = new Mock<IAsyncCursor<InstanceJournalEntry>>();
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true)
            .ReturnsAsync(false);
        cursor.SetupGet(c => c.Current)
            .Returns(journalExists ? [new InstanceJournalEntry { InstanceId = id, CurrentVersion = 3 }] : []);
        collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<InstanceJournalEntry>>(),
                It.IsAny<FindOptions<InstanceJournalEntry, InstanceJournalEntry>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor.Object);
        var writes = new List<(BsonDocument Filter, BsonDocument Update, bool IsUpsert)>();
        var registry = BsonSerializer.SerializerRegistry;
        var render = new RenderArgs<InstanceJournalEntry>(registry.GetSerializer<InstanceJournalEntry>(), registry);
        collection.Setup(c => c.UpdateOneAsync(It.IsAny<FilterDefinition<InstanceJournalEntry>>(),
                It.IsAny<UpdateDefinition<InstanceJournalEntry>>(), It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<InstanceJournalEntry>, UpdateDefinition<InstanceJournalEntry>, UpdateOptions,
                CancellationToken>((filter, update, options, _) => writes.Add((filter.Render(render),
                update.Render(render).AsBsonDocument, options.IsUpsert)))
            .ReturnsAsync(new UpdateResult.Acknowledged(mergeMatched ? 1 : 0, 0, null));
        BsonValue oldValue = isDate
            ? new BsonDateTime(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            : new BsonString("Old title");
        Assert.Equal(mergeMatched, await new InstanceJournalService(database.Object).LogPropertyChange(id,
            PropertyChangeEntry.Create("Value", oldValue, UnitTestsHelpers.AdminUser)));
        Assert.Equal(isDate || mergeMatched ? 1 : 2, writes.Count);
        if (!isDate)
        {
            var target = writes[0].Filter["PropertyChanges"]["$elemMatch"].AsBsonDocument;
            Assert.Equal(journalExists ? 3 : 0, target["Version"].AsInt32);
            Assert.Equal((int)BsonType.DateTime, target["OldValue"]["$not"]["$type"].AsInt32);
            Assert.False(writes[0].Update["$set"].AsBsonDocument.Contains("PropertyChanges.$.OldValue"));
        }

        if (mergeMatched) return;
        var append = writes.Last();
        Assert.Equal(oldValue, append.Update["$push"]["PropertyChanges"]["OldValue"]);
        Assert.True(append.IsUpsert);
        if (isDate) Assert.DoesNotContain(writes, write => write.Update.Contains("$set"));
    }
}