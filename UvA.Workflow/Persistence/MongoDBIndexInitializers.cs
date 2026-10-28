using UvA.Workflow.Migrations;
using UvA.Workflow.Messages;
using Organization = UvA.Workflow.Organizations.Organization;

namespace UvA.Workflow.Persistence;

public class MongoDbIndexInitializer(IMongoDatabase database)
{
    public async Task EnsureIndexes(CancellationToken ct = default)
    {
        await OrganizationsIndexes(ct);
        await MigrationIndexes(ct);
        await MessageIndexes(ct);
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

    private async Task MessageIndexes(CancellationToken ct)
    {
        var collection = database.GetCollection<Message>("messages");
        var keys = Builders<Message>.IndexKeys.Ascending(message => message.InstanceId);
        var options = new CreateIndexOptions { Name = "messages_instance_id" };

        await CreateOrUpdateIndexAsync(collection, new CreateIndexModel<Message>(keys, options), ct);
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