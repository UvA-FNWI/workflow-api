using UvA.Workflow.Migrations;
using Organization = UvA.Workflow.Organizations.Organization;

namespace UvA.Workflow.Persistence;

public class MongoDbInitializer(IMongoDatabase database)
{
    public async Task EnsureIndexes(CancellationToken ct = default)
    {
        await OrganizationsIndexes(ct);
        await MigrationIndexes(ct);
        await UserSearchIndex(ct);
    }

    public async Task BackfillUserSearchTerms(CancellationToken ct = default)
    {
        var users = database.GetCollection<User>("users");
        var filter = Builders<User>.Filter;
        var update = Builders<User>.Update;
        var missingTerms = filter.Eq(user => user.SearchTerms, null);
        using var cursor = await users.Find(missingTerms, new FindOptions { BatchSize = 500 })
            .SortBy(user => user.Id).ToCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        {
            var updates = new List<WriteModel<User>>();
            foreach (var user in cursor.Current)
            {
                // Recheck that the terms are missing and the source fields have not changed.
                updates.Add(new UpdateOneModel<User>(
                    missingTerms & filter.Eq(candidate => candidate.Id, user.Id) &
                    filter.Eq(candidate => candidate.DisplayName, user.DisplayName) &
                    filter.Eq(candidate => candidate.UserName, user.UserName) &
                    filter.Eq(candidate => candidate.Email, user.Email),
                    update.Set(candidate => candidate.SearchTerms, UserSearchTerms.Create(user))));
            }

            if (updates.Count > 0)
                await users.BulkWriteAsync(updates, new BulkWriteOptions { IsOrdered = false }, ct);
        }
    }

    private async Task UserSearchIndex(CancellationToken ct)
    {
        var users = database.GetCollection<User>("users");
        var index = new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(user => user.SearchTerms),
            new CreateIndexOptions { Name = "users_search_terms" });
        await CreateOrUpdateIndexAsync(users, index, ct);
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