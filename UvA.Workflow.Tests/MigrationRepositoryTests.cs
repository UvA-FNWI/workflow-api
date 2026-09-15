using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Journaling;
using UvA.Workflow.Migrations;
using UvA.Workflow.Persistence.Mongo;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class MigrationRepositoryTests
{
    [Fact]
    public void UsesSharedMigrationsCollection()
    {
        var collection = new Mock<IMongoCollection<Migration>>();
        var database = new Mock<IMongoDatabase>();
        database.Setup(value => value.GetCollection<Migration>("migrations",
                It.IsAny<MongoCollectionSettings>()))
            .Returns(collection.Object);

        _ = new MigrationRepository(database.Object);

        database.Verify(value => value.GetCollection<Migration>("migrations",
            It.IsAny<MongoCollectionSettings>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_FiltersUnsupportedStatusesInMongoBeforeDeserialization()
    {
        var fixture = new RepositoryFixture();
        using var cancellation = new CancellationTokenSource();
        var migrations = new[] { MigrationStatus.Applying, MigrationStatus.Finished, MigrationStatus.Failed }
            .Select(status => new Migration { Status = status }).ToArray();
        var cursor = new Mock<IAsyncCursor<Migration>>();
        cursor.SetupGet(value => value.Current).Returns(migrations);
        cursor.SetupSequence(value => value.MoveNextAsync(cancellation.Token))
            .ReturnsAsync(true).ReturnsAsync(false);
        FilterDefinition<Migration>? capturedFilter = null;
        SortDefinition<Migration>? capturedSort = null;
        fixture.Migrations.Setup(value => value.FindAsync(It.IsAny<FilterDefinition<Migration>>(),
                It.IsAny<FindOptions<Migration, Migration>>(), cancellation.Token))
            .Callback<FilterDefinition<Migration>, FindOptions<Migration, Migration>, CancellationToken>((filter,
                options, _) =>
            {
                capturedFilter = filter;
                capturedSort = options.Sort;
            })
            .ReturnsAsync(cursor.Object);

        var result = await fixture.Repository.GetAll(cancellation.Token);

        Assert.Equal(migrations, result);
        Assert.Equal(new BsonDocument("Status",
                new BsonDocument("$in", new BsonArray { "Applying", "Finished", "Failed" })),
            RenderFilter(capturedFilter!));
        Assert.Equal(new BsonDocument("RequestedAt", -1), capturedSort!.Render(
            new RenderArgs<Migration>(BsonSerializer.LookupSerializer<Migration>(),
                BsonSerializer.SerializerRegistry)));
    }

    [Theory]
    [InlineData(7, 4)]
    [InlineData(0, 0)]
    public async Task RenamePropertyValues_RenamesOnlyExistingSourcePropertiesInTargetWorkflows(
        long matched, long modified)
    {
        var fixture = new RepositoryFixture();
        using var cancellation = new CancellationTokenSource();
        FilterDefinition<WorkflowInstance>? capturedFilter = null;
        UpdateDefinition<WorkflowInstance>? capturedUpdate = null;
        fixture.Instances.Setup(value => value.UpdateManyAsync(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<UpdateOptions>(), cancellation.Token))
            .Callback<FilterDefinition<WorkflowInstance>, UpdateDefinition<WorkflowInstance>, UpdateOptions,
                CancellationToken>((filter, update, _, _) =>
            {
                capturedFilter = filter;
                capturedUpdate = update;
            })
            .ReturnsAsync(new UpdateResult.Acknowledged(matched, modified, null));

        var result = await fixture.Repository.RenamePropertyValues(RenameMigration(), cancellation.Token);

        Assert.Equal(matched, result.InstancesMatched);
        Assert.Equal(modified, result.InstancesUpdated);
        Assert.Equal(new BsonDocument
        {
            { "WorkflowDefinition", new BsonDocument("$in", new BsonArray { "Project", "Course" }) },
            { "Properties.Title", new BsonDocument("$exists", true) }
        }, RenderFilter(capturedFilter!));
        Assert.Equal(new BsonDocument("$rename", new BsonDocument("Properties.Title", "Properties.ProjectTitle")),
            RenderUpdate(capturedUpdate!));
        fixture.Instances.Verify(value => value.CountDocumentsAsync(
            It.IsAny<FilterDefinition<WorkflowInstance>>(), It.IsAny<CountOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenameJournalPaths_SetsOnlyMatchingPathsAndPreservesTheirSuffixes()
    {
        var first = Journal("Title", "TitleOther", "Title.DisplayName", "Nested.Title", "Title.0.Name", "title",
            "ProjectTitle");
        var second = Journal("Title");
        var unrelated = Journal("OtherTitle");
        var originalFirst = first.ToBsonDocument();
        var fixture = new RepositoryFixture([first.InstanceId, second.InstanceId, unrelated.InstanceId],
            [first, second, unrelated]);

        var renamed = await fixture.Repository.RenameJournalPaths(RenameMigration());

        Assert.Equal(4, renamed);
        Assert.Equal(new BsonDocument("WorkflowDefinition",
                new BsonDocument("$in", new BsonArray { "Project", "Course" })),
            RenderFilter(fixture.InstanceFilter!));
        Assert.Equal(new BsonDocument("_id", new BsonDocument("$in", new BsonArray
        {
            new ObjectId(first.InstanceId), new ObjectId(second.InstanceId), new ObjectId(unrelated.InstanceId)
        })), RenderFilter(fixture.JournalFilter!));
        Assert.Collection(fixture.Writes,
            write =>
            {
                var update = Assert.IsType<UpdateOneModel<InstanceJournalEntry>>(write);
                Assert.Equal(new BsonDocument("_id", new ObjectId(first.InstanceId)), RenderFilter(update.Filter));
                Assert.Equal(new BsonDocument("$set", new BsonDocument
                {
                    { "PropertyChanges.0.Path", "ProjectTitle" },
                    { "PropertyChanges.2.Path", "ProjectTitle.DisplayName" },
                    { "PropertyChanges.4.Path", "ProjectTitle.0.Name" }
                }), RenderUpdate(update.Update));
                Assert.False(update.IsUpsert);
            },
            write =>
            {
                var update = Assert.IsType<UpdateOneModel<InstanceJournalEntry>>(write);
                Assert.Equal(new BsonDocument("_id", new ObjectId(second.InstanceId)), RenderFilter(update.Filter));
                Assert.Equal(new BsonDocument("$set", new BsonDocument("PropertyChanges.0.Path", "ProjectTitle")),
                    RenderUpdate(update.Update));
                Assert.False(update.IsUpsert);
            });
        Assert.Equal(originalFirst, first.ToBsonDocument());
    }

    [Fact]
    public async Task RenameJournalPaths_RetryAfterPartialBulkWriteOnlyRenamesRemainingPaths()
    {
        var first = Journal("Title.DisplayName");
        var second = Journal("Title");
        var journals = new[] { first, second };
        var fixture = new RepositoryFixture([first.InstanceId, second.InstanceId], journals);
        var attempts = 0;
        fixture.Journals.Setup(value => value.BulkWriteAsync(It.IsAny<IEnumerable<WriteModel<InstanceJournalEntry>>>(),
                It.IsAny<BulkWriteOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<WriteModel<InstanceJournalEntry>>, BulkWriteOptions,
                CancellationToken>((writes, _, _) =>
            {
                var updates = writes.Cast<UpdateOneModel<InstanceJournalEntry>>().ToArray();
                if (attempts++ == 0)
                {
                    Assert.Equal(2, updates.Length);
                    var document = first.ToBsonDocument();
                    document["PropertyChanges"][0]["Path"] =
                        RenderUpdate(updates[0].Update)["$set"]["PropertyChanges.0.Path"];
                    journals[0] = BsonSerializer.Deserialize<InstanceJournalEntry>(document);
                    throw new InvalidOperationException("Interrupted after the first journal write");
                }

                var remaining = Assert.Single(updates);
                Assert.Equal(new BsonDocument("_id", new ObjectId(second.InstanceId)), RenderFilter(remaining.Filter));
                var secondDocument = second.ToBsonDocument();
                secondDocument["PropertyChanges"][0]["Path"] =
                    RenderUpdate(remaining.Update)["$set"]["PropertyChanges.0.Path"];
                journals[1] = BsonSerializer.Deserialize<InstanceJournalEntry>(secondDocument);
            })
            .ReturnsAsync(new BulkWriteResult<InstanceJournalEntry>.Acknowledged(0, 0, 0, 0, 0, [], []));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.RenameJournalPaths(RenameMigration()));
        Assert.Equal(1, await fixture.Repository.RenameJournalPaths(RenameMigration()));
        Assert.Equal(0, await fixture.Repository.RenameJournalPaths(RenameMigration()));

        Assert.Equal("ProjectTitle.DisplayName", journals[0].PropertyChanges[0].Path);
        Assert.Equal("ProjectTitle", journals[1].PropertyChanges[0].Path);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RenameJournalPaths_WithoutTargetInstancesDoesNotReadOrWriteJournals()
    {
        var fixture = new RepositoryFixture();

        var renamed = await fixture.Repository.RenameJournalPaths(RenameMigration());

        Assert.Equal(0, renamed);
        fixture.Journals.Verify(value => value.FindAsync(
            It.IsAny<FilterDefinition<InstanceJournalEntry>>(),
            It.IsAny<FindOptions<InstanceJournalEntry, InstanceJournalEntry>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.VerifyNoJournalWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenameJournalPaths_WithoutMatchingPathsDoesNotWriteJournals(bool hasJournal)
    {
        var journal = Journal("TitleOther", "Nested.Title", "title", "ProjectTitle");
        var fixture = new RepositoryFixture([journal.InstanceId], hasJournal ? [journal] : []);

        var renamed = await fixture.Repository.RenameJournalPaths(RenameMigration());

        Assert.Equal(0, renamed);
        fixture.VerifyNoJournalWrites();
    }

    private static Migration RenameMigration() => new()
    {
        WorkflowDefinitions = ["Project", "Course"],
        OldProperty = "Title",
        NewProperty = "ProjectTitle"
    };

    private static InstanceJournalEntry Journal(params string[] paths) => new()
    {
        InstanceId = ObjectId.GenerateNewId().ToString(),
        CurrentVersion = 5,
        PropertyChanges = paths.Select(path => PropertyChangeEntry.Create(path,
            new BsonDocument("value", "previous value"), new User { UserName = "editor" })).ToArray()
    };

    private static BsonDocument RenderFilter<T>(FilterDefinition<T> filter)
        => filter.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry));

    private static BsonDocument RenderUpdate<T>(UpdateDefinition<T> update)
        => update.Render(new RenderArgs<T>(BsonSerializer.LookupSerializer<T>(), BsonSerializer.SerializerRegistry))
            .AsBsonDocument;

    private sealed class RepositoryFixture
    {
        public Mock<IMongoCollection<Migration>> Migrations { get; } = new();
        public Mock<IMongoCollection<WorkflowInstance>> Instances { get; } = new();
        public Mock<IMongoCollection<InstanceJournalEntry>> Journals { get; } = new();
        public MigrationRepository Repository { get; }
        public FilterDefinition<WorkflowInstance>? InstanceFilter { get; private set; }
        public FilterDefinition<InstanceJournalEntry>? JournalFilter { get; private set; }
        public WriteModel<InstanceJournalEntry>[] Writes { get; private set; } = [];

        public RepositoryFixture(string[]? instanceIds = null, InstanceJournalEntry[]? journals = null)
        {
            Instances.Setup(value => value.FindAsync(It.IsAny<FilterDefinition<WorkflowInstance>>(),
                    It.IsAny<FindOptions<WorkflowInstance, string>>(), It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<WorkflowInstance>, FindOptions<WorkflowInstance, string>,
                    CancellationToken>((filter, _, _) => InstanceFilter = filter)
                .ReturnsAsync(() => Cursor(instanceIds ?? []));
            Journals.Setup(value => value.FindAsync(It.IsAny<FilterDefinition<InstanceJournalEntry>>(),
                    It.IsAny<FindOptions<InstanceJournalEntry, InstanceJournalEntry>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<FilterDefinition<InstanceJournalEntry>,
                    FindOptions<InstanceJournalEntry, InstanceJournalEntry>, CancellationToken>((filter, _, _) =>
                    JournalFilter = filter)
                .ReturnsAsync(() => Cursor(journals ?? []));
            Journals.Setup(value => value.BulkWriteAsync(It.IsAny<IEnumerable<WriteModel<InstanceJournalEntry>>>(),
                    It.IsAny<BulkWriteOptions>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<WriteModel<InstanceJournalEntry>>, BulkWriteOptions, CancellationToken>((writes,
                    _, _) => Writes = writes.ToArray())
                .ReturnsAsync(new BulkWriteResult<InstanceJournalEntry>.Acknowledged(0, 0, 0, 0, 0, [], []));

            var database = new Mock<IMongoDatabase>();
            database.Setup(value => value.GetCollection<Migration>("migrations", It.IsAny<MongoCollectionSettings>()))
                .Returns(Migrations.Object);
            database.Setup(value => value.GetCollection<WorkflowInstance>("instances",
                    It.IsAny<MongoCollectionSettings>()))
                .Returns(Instances.Object);
            database.Setup(value => value.GetCollection<InstanceJournalEntry>("instance_journal",
                    It.IsAny<MongoCollectionSettings>()))
                .Returns(Journals.Object);
            Repository = new MigrationRepository(database.Object);
        }

        public void VerifyNoJournalWrites()
            => Journals.Verify(value => value.BulkWriteAsync(
                It.IsAny<IEnumerable<WriteModel<InstanceJournalEntry>>>(), It.IsAny<BulkWriteOptions>(),
                It.IsAny<CancellationToken>()), Times.Never);

        private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> values)
        {
            var cursor = new Mock<IAsyncCursor<T>>();
            cursor.SetupGet(value => value.Current).Returns(values);
            cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true).ReturnsAsync(false);
            return cursor.Object;
        }
    }
}