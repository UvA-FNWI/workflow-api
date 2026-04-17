using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Users;

public class UserRepositoryTests
{
    [Fact]
    public async Task Update_UsesObjectIdFilter()
    {
        var collectionMock = new Mock<IMongoCollection<User>>();
        var databaseMock = new Mock<IMongoDatabase>();
        FilterDefinition<User>? capturedFilter = null;

        collectionMock.Setup(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<User>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<User>, User, ReplaceOptions, CancellationToken>((filter, _, _, _) =>
                capturedFilter = filter)
            .ReturnsAsync(CreateReplaceOneResult(matchedCount: 1));

        databaseMock.Setup(d => d.GetCollection<User>("users", It.IsAny<MongoCollectionSettings>()))
            .Returns(collectionMock.Object);

        var repository = new UserRepository(databaseMock.Object);
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "eduid-123",
            DisplayName = "External User",
            Email = "external@example.org"
        };

        await repository.Update(user, CancellationToken.None);

        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var serializer = serializerRegistry.GetSerializer<User>();
        var renderedFilter = capturedFilter!.Render(new RenderArgs<User>(serializer,
            serializerRegistry,
            new PathRenderArgs(string.Empty, false),
            renderDollarForm: false,
            renderForFind: false,
            renderForElemMatch: false,
            translationOptions: null));

        Assert.Equal(BsonType.ObjectId, renderedFilter["_id"].BsonType);
        Assert.Equal(new ObjectId(user.Id), renderedFilter["_id"].AsObjectId);
    }

    [Fact]
    public async Task Update_InvalidId_Throws()
    {
        var collectionMock = new Mock<IMongoCollection<User>>();
        var databaseMock = new Mock<IMongoDatabase>();
        databaseMock.Setup(d => d.GetCollection<User>("users", It.IsAny<MongoCollectionSettings>()))
            .Returns(collectionMock.Object);

        var repository = new UserRepository(databaseMock.Object);
        var user = new User
        {
            Id = "not-an-object-id",
            UserName = "eduid-123",
            DisplayName = "External User",
            Email = "external@example.org"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => repository.Update(user, CancellationToken.None));
        collectionMock.Verify(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<User>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_WhenUserIsMissing_Throws()
    {
        var collectionMock = new Mock<IMongoCollection<User>>();
        var databaseMock = new Mock<IMongoDatabase>();

        collectionMock.Setup(c => c.ReplaceOneAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<User>(),
                It.IsAny<ReplaceOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateReplaceOneResult(matchedCount: 0));

        databaseMock.Setup(d => d.GetCollection<User>("users", It.IsAny<MongoCollectionSettings>()))
            .Returns(collectionMock.Object);

        var repository = new UserRepository(databaseMock.Object);
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserName = "eduid-123",
            DisplayName = "External User",
            Email = "external@example.org"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Update(user, CancellationToken.None));
    }

    private static ReplaceOneResult CreateReplaceOneResult(long matchedCount)
    {
        var replaceResultMock = new Mock<ReplaceOneResult>();
        replaceResultMock.SetupGet(r => r.IsAcknowledged).Returns(true);
        replaceResultMock.SetupGet(r => r.MatchedCount).Returns(matchedCount);
        replaceResultMock.SetupGet(r => r.ModifiedCount).Returns(matchedCount);
        return replaceResultMock.Object;
    }
}