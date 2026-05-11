namespace UvA.Workflow.Persistence;

public class MongoDbIndexInitializer(IMongoDatabase database)
{
    public async Task EnsureIndexes(CancellationToken ct = default)
    {
        await OrganizationsIndexes(ct);
    }

    private async Task OrganizationsIndexes(CancellationToken ct = default)
    {
        var collection = database.GetCollection<Organization>("organizations");
        var keys = Builders<Organization>.IndexKeys.Ascending(o => o.Name);
        var options = new CreateIndexOptions { Name = "organizations_name" };

        await collection.Indexes.CreateOneAsync(new CreateIndexModel<Organization>(keys, options),
            cancellationToken: ct);
    }
}