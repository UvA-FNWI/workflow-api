using UvA.Workflow.Migrations;

namespace UvA.Workflow.Persistence.Mongo;

public class MigrationRepository(IMongoDatabase database) : IMigrationRepository
{
    private readonly IMongoCollection<Migration> _collection =
        database.GetCollection<Migration>("migrations");

    private readonly IMongoCollection<WorkflowInstance> _instances =
        database.GetCollection<WorkflowInstance>("instances");

    private readonly IMongoCollection<InstanceJournalEntry> _journals =
        database.GetCollection<InstanceJournalEntry>("instance_journal");

    public async Task<IReadOnlyList<Migration>> GetAll(CancellationToken ct = default)
        => await _collection.Find(Builders<Migration>.Filter.Empty)
            .SortByDescending(migration => migration.RequestedAt)
            .ToListAsync(ct);

    public async Task<Migration?> GetByMigrationId(string migrationId, CancellationToken ct = default)
        => await _collection.Find(migration => migration.MigrationId == migrationId).FirstOrDefaultAsync(ct);

    public Task Create(Migration migration, CancellationToken ct = default)
        => _collection.InsertOneAsync(migration, cancellationToken: ct);

    public async Task Update(Migration migration, CancellationToken ct = default)
    {
        var result = await _collection.ReplaceOneAsync(value => value.MigrationId == migration.MigrationId, migration,
            cancellationToken: ct);
        if (result.MatchedCount == 0)
            throw new InvalidOperationException($"Migration '{migration.MigrationId}' does not exist");
    }

    public async Task<PropertyRenameResult> RenamePropertyValues(Migration migration,
        CancellationToken ct = default)
    {
        var sourceFilter = Builders<WorkflowInstance>.Filter.And(
            Builders<WorkflowInstance>.Filter.In(value => value.WorkflowDefinition,
                migration.WorkflowDefinitions),
            Builders<WorkflowInstance>.Filter.Exists($"Properties.{migration.OldProperty}"));
        var update = Builders<WorkflowInstance>.Update.Rename(
            $"Properties.{migration.OldProperty}", $"Properties.{migration.NewProperty}");
        var result = await _instances.UpdateManyAsync(sourceFilter, update, cancellationToken: ct);
        return new PropertyRenameResult(result.MatchedCount, result.ModifiedCount);
    }

    public async Task<long> RenameJournalPaths(Migration migration, CancellationToken ct = default)
    {
        var instanceIds = await _instances
            .Find(Builders<WorkflowInstance>.Filter.In(value => value.WorkflowDefinition,
                migration.WorkflowDefinitions))
            .Project(value => value.Id)
            .ToListAsync(ct);
        if (instanceIds.Count == 0)
            return 0;

        var journals = await _journals.Find(
                Builders<InstanceJournalEntry>.Filter.In(value => value.InstanceId, instanceIds))
            .ToListAsync(ct);
        var writes = new List<WriteModel<InstanceJournalEntry>>();
        long renamed = 0;
        foreach (var journal in journals)
        {
            var updates = new List<UpdateDefinition<InstanceJournalEntry>>();
            for (var index = 0; index < journal.PropertyChanges.Length; index++)
            {
                var propertyChange = journal.PropertyChanges[index];
                if (propertyChange.Path != migration.OldProperty &&
                    !propertyChange.Path.StartsWith(migration.OldProperty + '.', StringComparison.Ordinal))
                    continue;

                updates.Add(Builders<InstanceJournalEntry>.Update.Set(
                    $"PropertyChanges.{index}.Path",
                    migration.NewProperty + propertyChange.Path[migration.OldProperty.Length..]));
                renamed++;
            }

            if (updates.Count > 0)
                writes.Add(new UpdateOneModel<InstanceJournalEntry>(
                    Builders<InstanceJournalEntry>.Filter.Eq(value => value.InstanceId, journal.InstanceId),
                    Builders<InstanceJournalEntry>.Update.Combine(updates)));
        }

        if (writes.Count > 0)
            await _journals.BulkWriteAsync(writes, cancellationToken: ct);
        return renamed;
    }
}