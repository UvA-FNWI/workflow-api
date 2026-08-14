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

    public async Task<Migration> EnsureCreated(Migration migration, CancellationToken ct = default)
    {
        var update = Builders<Migration>.Update
            .SetOnInsert(value => value.Id, migration.Id)
            .SetOnInsert(value => value.Kind, migration.Kind)
            .SetOnInsert(value => value.WorkflowDefinition, migration.WorkflowDefinition)
            .SetOnInsert(value => value.OldPath, migration.OldPath)
            .SetOnInsert(value => value.NewPath, migration.NewPath)
            .SetOnInsert(value => value.Description, migration.Description)
            .SetOnInsert(value => value.SourceCommit, migration.SourceCommit)
            .SetOnInsert(value => value.TargetCommit, migration.TargetCommit)
            .SetOnInsert(value => value.Stage, migration.Stage)
            .SetOnInsert(value => value.RunStatus, migration.RunStatus)
            .SetOnInsert(value => value.RequestedBy, migration.RequestedBy)
            .SetOnInsert(value => value.RequestedAt, migration.RequestedAt)
            .SetOnInsert(value => value.Checkpoint, migration.Checkpoint)
            .SetOnInsert(value => value.Counts, migration.Counts)
            .SetOnInsert(value => value.AttentionReason, migration.AttentionReason);

        var stored = await _collection.FindOneAndUpdateAsync(
            value => value.Id == migration.Id,
            update,
            new FindOneAndUpdateOptions<Migration> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            ct);

        if (stored.Kind != migration.Kind ||
            stored.WorkflowDefinition != migration.WorkflowDefinition ||
            stored.OldPath != migration.OldPath ||
            stored.NewPath != migration.NewPath ||
            stored.TargetCommit != migration.TargetCommit)
            throw new InvalidOperationException(
                $"Migration '{migration.Id}' already exists with a different definition");

        return stored;
    }
}