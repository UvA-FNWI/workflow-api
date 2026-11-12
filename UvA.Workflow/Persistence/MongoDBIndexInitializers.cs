using UvA.Workflow.Migrations;
using Organization = UvA.Workflow.Organizations.Organization;

namespace UvA.Workflow.Persistence;

public class MongoDbIndexInitializer(IMongoDatabase database)
{
    public async Task EnsureIndexes(CancellationToken ct = default)
    {
        await OrganizationsIndexes(ct);
        await MigrationIndexes(ct);
        await UserSearchIndex(ct);
    }

    private async Task UserSearchIndex(CancellationToken ct)
    {
        var collection = database.GetCollection<User>("users");
        var options = new CreateIndexOptions { Name = "users_search_terms" };
        await CreateOrUpdateIndexAsync(collection,
            new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(user => user.SearchTerms), options), ct);

        var missingTerms = Builders<User>.Filter.Eq(user => user.SearchTerms, null);
        using var users = await collection.Find(missingTerms, new FindOptions { BatchSize = 500 }).ToCursorAsync(ct);
        while (await users.MoveNextAsync(ct))
        {
            var updates = users.Current.Select(user => new UpdateOneModel<User>(
                    Builders<User>.Filter.Eq(candidate => candidate.Id, user.Id) & missingTerms,
                    Builders<User>.Update.Set(candidate => candidate.SearchTerms, UserSearchTerms.Create(user))))
                .ToArray();
            if (updates.Length > 0)
                await collection.BulkWriteAsync(updates, new BulkWriteOptions { IsOrdered = false }, ct);
        }
    }

    private async Task MigrationIndexes(CancellationToken ct)
    {
        var collection = database.GetCollection<Migration>("migrations");
        var keys = Builders<Migration>.IndexKeys.Ascending(migration => migration.MigrationId);
        var options = new CreateIndexOptions { Name = "migrations_migration_id", Unique = true };

        await CreateOrUpdateIndexAsync(collection, new CreateIndexModel<Migration>(keys, options), ct);
    }

    private async Task OrganizationsIndexes(CancellationToken ct)
    {
        var collection = database.GetCollection<Organization>("organizations");
        var keys = Builders<Organization>.IndexKeys.Ascending(organization => organization.Name);
        var options = new CreateIndexOptions { Name = "organizations_name" };

        await CreateOrUpdateIndexAsync(collection, new CreateIndexModel<Organization>(keys, options), ct);
    }

    private static async Task CreateOrUpdateIndexAsync<TDocument>(IMongoCollection<TDocument> collection,
        CreateIndexModel<TDocument> model, CancellationToken ct)
    {
        try
        {
            await collection.Indexes.CreateOneAsync(model, cancellationToken: ct);
        }
        catch (MongoCommandException exception) when (exception.CodeName == "IndexKeySpecsConflict")
        {
            await collection.Indexes.DropOneAsync(model.Options.Name, cancellationToken: ct);
            await collection.Indexes.CreateOneAsync(model, cancellationToken: ct);
        }
    }
}