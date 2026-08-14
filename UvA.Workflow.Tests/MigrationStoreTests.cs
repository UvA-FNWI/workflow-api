using MongoDB.Driver;
using Moq;
using UvA.Workflow.Migrations;
using UvA.Workflow.Persistence.Mongo;

namespace UvA.Workflow.Tests;

public class MigrationStoreTests
{
    [Fact]
    public void UsesSharedMigrationsCollection()
    {
        var collection = new Mock<IMongoCollection<Migration>>();
        var database = new Mock<IMongoDatabase>();
        database.Setup(value => value.GetCollection<Migration>("migrations",
                It.IsAny<MongoCollectionSettings>()))
            .Returns(collection.Object);

        _ = new MigrationStore(database.Object);

        database.Verify(value => value.GetCollection<Migration>("migrations",
            It.IsAny<MongoCollectionSettings>()), Times.Once);
    }
}