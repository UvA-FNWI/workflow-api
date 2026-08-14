using UvA.Workflow.Migrations;

namespace UvA.Workflow.Persistence.Mongo;

public class MigrationStore(IMongoDatabase database) : IMigrationStore
{
    private readonly IMongoCollection<Migration> _collection =
        database.GetCollection<Migration>("migrations");

    public async Task<IReadOnlyList<Migration>> GetAll(CancellationToken ct = default)
        => await _collection.Find(Builders<Migration>.Filter.Empty)
            .SortBy(migration => migration.RequestedAt)
            .ToListAsync(ct);

    public async Task<Migration?> GetById(string id, CancellationToken ct = default)
        => await _collection.Find(migration => migration.Id == id).FirstOrDefaultAsync(ct);
}