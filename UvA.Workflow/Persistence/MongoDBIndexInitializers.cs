using UvA.Workflow.Organisations;

namespace UvA.Workflow.Persistence;

public class MongoDbIndexInitializer(IMongoDatabase database)
{
    public async Task EnsureIndexes(CancellationToken ct = default)
    {
        await OrganisationsIndexes(ct);
    }

    private async Task OrganisationsIndexes(CancellationToken ct = default)
    {
        var collection = database.GetCollection<Organisation>("organisations");

        var keys = Builders<Organisation>.IndexKeys.Ascending(o => o.Name);

        var options = new CreateIndexOptions { Name = "organisations_name" };

        await collection.Indexes.CreateOneAsync(new CreateIndexModel<Organisation>(keys, options),
            cancellationToken: ct);
    }
}