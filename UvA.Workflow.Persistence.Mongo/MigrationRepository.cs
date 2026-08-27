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

    public async Task<Migration?> GetById(string id, CancellationToken ct = default)
        => await _collection.Find(migration => migration.Id == id).FirstOrDefaultAsync(ct);

    public Task Create(Migration migration, CancellationToken ct = default)
        => _collection.InsertOneAsync(migration, cancellationToken: ct);

    public async Task Update(Migration migration, CancellationToken ct = default)
    {
        var result = await _collection.ReplaceOneAsync(value => value.Id == migration.Id, migration,
            cancellationToken: ct);
        if (result.MatchedCount == 0)
            throw new InvalidOperationException($"Migration '{migration.Id}' does not exist");
    }

    public Task<long> CountTargetFields(Migration migration, CancellationToken ct = default)
    {
        var definition = GetRenamePropertyDefinition(migration);
        return _instances.CountDocumentsAsync(Builders<WorkflowInstance>.Filter.And(
                Builders<WorkflowInstance>.Filter.In(value => value.WorkflowDefinition,
                    definition.WorkflowDefinitions),
                Builders<WorkflowInstance>.Filter.Exists($"Properties.{definition.NewProperty}")),
            cancellationToken: ct);
    }

    public async Task<PropertyCopyResult> CopyPropertyValues(Migration migration, bool overwriteTarget,
        CancellationToken ct = default)
    {
        var definition = GetRenamePropertyDefinition(migration);
        var sourceFilter = Builders<WorkflowInstance>.Filter.And(
            Builders<WorkflowInstance>.Filter.In(value => value.WorkflowDefinition,
                definition.WorkflowDefinitions),
            Builders<WorkflowInstance>.Filter.Exists($"Properties.{definition.OldProperty}"));
        var filter = overwriteTarget
            ? sourceFilter
            : Builders<WorkflowInstance>.Filter.And(sourceFilter,
                Builders<WorkflowInstance>.Filter.Exists($"Properties.{definition.NewProperty}", false));
        var matched = await _instances.CountDocumentsAsync(sourceFilter, cancellationToken: ct);
        PipelineDefinition<WorkflowInstance, WorkflowInstance> pipeline = new[]
        {
            new BsonDocument("$set", new BsonDocument(
                $"Properties.{definition.NewProperty}", $"$Properties.{definition.OldProperty}"))
        };
        var update = new PipelineUpdateDefinition<WorkflowInstance>(pipeline);
        var result = await _instances.UpdateManyAsync(filter, update, cancellationToken: ct);
        return new PropertyCopyResult(matched, result.ModifiedCount);
    }

    public async Task<long> RemoveTargetFields(Migration migration, CancellationToken ct = default)
    {
        var definition = GetRenamePropertyDefinition(migration);
        var filter = Builders<WorkflowInstance>.Filter.And(
            Builders<WorkflowInstance>.Filter.In(value => value.WorkflowDefinition,
                definition.WorkflowDefinitions),
            Builders<WorkflowInstance>.Filter.Exists($"Properties.{definition.NewProperty}"));
        var result = await _instances.UpdateManyAsync(filter,
            Builders<WorkflowInstance>.Update.Unset($"Properties.{definition.NewProperty}"),
            cancellationToken: ct);
        return result.ModifiedCount;
    }

    public async Task<long> RenameJournalPaths(Migration migration, CancellationToken ct = default)
    {
        var definition = GetRenamePropertyDefinition(migration);
        var instanceIds = await _instances
            .Find(Builders<WorkflowInstance>.Filter.In(value => value.WorkflowDefinition,
                definition.WorkflowDefinitions))
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
            var changed = false;
            foreach (var propertyChange in journal.PropertyChanges)
            {
                if (propertyChange.Path != definition.OldProperty &&
                    !propertyChange.Path.StartsWith(definition.OldProperty + '.', StringComparison.Ordinal))
                    continue;

                propertyChange.RenamePath(
                    definition.NewProperty + propertyChange.Path[definition.OldProperty.Length..]);
                renamed++;
                changed = true;
            }

            if (changed)
                writes.Add(new ReplaceOneModel<InstanceJournalEntry>(
                    Builders<InstanceJournalEntry>.Filter.Eq(value => value.InstanceId, journal.InstanceId),
                    journal));
        }

        if (writes.Count > 0)
            await _journals.BulkWriteAsync(writes, cancellationToken: ct);
        return renamed;
    }

    private static RenamePropertyDefinition GetRenamePropertyDefinition(Migration migration)
        => migration.Definition as RenamePropertyDefinition
           ?? throw new InvalidOperationException(
               $"Migration '{migration.Id}' does not contain a property rename definition");
}