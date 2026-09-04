using System.Diagnostics;
using Microsoft.Graph.Models;
using UvA.Workflow.Migrations;
using Organization = UvA.Workflow.Organizations.Organization;

namespace UvA.Workflow.Persistence;

public class MongoDbIndexInitializer(IMongoDatabase database)
{
    public async Task EnsureIndexes(CancellationToken ct = default)
    {
        await OrganizationsIndexes(ct);
        await MigrationIndexes(ct);
    }

    private async Task MigrationIndexes(CancellationToken ct = default)
    {
        var collection = database.GetCollection<Migration>("migrations");
        var keys = Builders<Migration>.IndexKeys.Ascending(migration => migration.MigrationId);
        var options = new CreateIndexOptions { Name = "migrations_migration_id", Unique = true };

        await CreateOrUpdateIndexAsync(collection, new CreateIndexModel<Migration>(keys, options), ct);
    }

    private async Task OrganizationsIndexes(CancellationToken ct = default)
    {
        var collection = database.GetCollection<Organization>("organizations");
        var keys = Builders<Organization>.IndexKeys.Ascending(o => o.Name);
        var options = new CreateIndexOptions { Name = "organizations_name" };

        await CreateOrUpdateIndexAsync(collection, new CreateIndexModel<Organization>(keys, options), ct);
    }


    private async Task CreateOrUpdateIndexAsync<TDocument>(IMongoCollection<TDocument> collection,
        CreateIndexModel<TDocument> model, CancellationToken ct = default)
    {
        try
        {
            await collection.Indexes.CreateOneAsync(model, cancellationToken: ct);
        }
        catch (MongoCommandException ex)
        {
            if (ex.CodeName == "IndexKeySpecsConflict")
            {
                // Drop the index and recreate it with updated properties. (Only applies when the Index specifications are different/updated)
                await collection.Indexes.DropOneAsync(model.Options.Name, cancellationToken: ct);
                await collection.Indexes.CreateOneAsync(model, cancellationToken: ct);
            }
        }
    }
}