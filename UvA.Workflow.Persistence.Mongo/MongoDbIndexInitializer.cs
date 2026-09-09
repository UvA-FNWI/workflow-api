using UvA.Workflow.Organizations;

namespace UvA.Workflow.Persistence.Mongo;

public class MongoDbIndexInitializer(IMongoDatabase database)
{
    public async Task EnsureIndexes(CancellationToken ct = default)
    {
        await OrganizationsIndexes(ct);
        await EventLogIndexes(ct);
    }

    private async Task EventLogIndexes(CancellationToken ct)
    {
        var collection = database.GetCollection<InstanceEventLogEntry>("eventlog");
        var keys = Builders<InstanceEventLogEntry>.IndexKeys
            .Ascending(entry => entry.WorkflowInstanceId)
            .Ascending(entry => entry.OperationMetadata!.TopLevelStep)
            .Ascending(entry => entry.OperationMetadata!.Revision);
        var options = new CreateIndexOptions<InstanceEventLogEntry>
        {
            Name = "eventlog_operation_revision",
            Unique = true,
            PartialFilterExpression = Builders<InstanceEventLogEntry>.Filter.Exists(entry => entry.OperationMetadata)
        };

        await CreateOrUpdateIndexAsync(collection, new CreateIndexModel<InstanceEventLogEntry>(keys, options), ct);
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