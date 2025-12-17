using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Auditing;

public interface IAuditLogService
{
    Task<WorkflowInstanceChangeSet?> GetInstanceChangeSet(string instanceId, CancellationToken ct = default);

    Task LogPropertyChange(string instanceId, PropertyValueChange valueChange, CancellationToken ct = default);

    Task LogPropertyChanges(string instanceId, IEnumerable<PropertyValueChange> newChanges,
        CancellationToken ct = default);

    Task<int> IncrementVersion(string instanceId, CancellationToken ct = default);
}

public class AuditLogService(IMongoDatabase db) : IAuditLogService
{
    private static readonly TimeSpan PropertyChangeMergeWindow = TimeSpan.FromMinutes(5);

    private readonly IMongoCollection<WorkflowInstanceChangeSet> _changeSetCollection =
        db.GetCollection<WorkflowInstanceChangeSet>("instance_changes");

    public async Task<WorkflowInstanceChangeSet?> GetInstanceChangeSet(string instanceId,
        CancellationToken ct = default)
    {
        var filter = Builders<WorkflowInstanceChangeSet>.Filter.Eq(x => x.InstanceId, instanceId);
        return await _changeSetCollection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task LogPropertyChange(string instanceId, PropertyValueChange valueChange,
        CancellationToken ct = default)
    {
        await LogPropertyChanges(instanceId, [valueChange], ct);
    }

    public async Task LogPropertyChanges(string instanceId, IEnumerable<PropertyValueChange> newChanges,
        CancellationToken ct = default)
    {
        var changeSet = await GetInstanceChangeSet(instanceId, ct);
        if (changeSet == null)
            throw new EntityNotFoundException("WorkflowInstanceChangeSet", $"instanceId:{instanceId}");

        // Only start tracking changes after the first version, discard before that
        if (changeSet.CurrentVersion == 0) return;

        var changes = newChanges.Where(nc => nc.OldValue != nc.NewValue).ToList();
        if (changes.Count == 0)
            return;

        foreach (var change in changes)
            change.Version = changeSet.CurrentVersion;

        var instanceIdFilter = Builders<WorkflowInstanceChangeSet>.Filter.Eq(x => x.InstanceId, instanceId);

        foreach (var change in changes)
        {
            var cutoff = change.Timestamp.Subtract(PropertyChangeMergeWindow);

            var matchExistingFilter =
                instanceIdFilter &
                Builders<WorkflowInstanceChangeSet>.Filter.ElemMatch(
                    x => x.PropertyChanges,
                    pc => pc.Version == change.Version &&
                          pc.Path == change.Path &&
                          pc.Timestamp >= cutoff
                );

            // Update the matched array element in-place
            // Note: using string paths so we can target the positional "$" element.
            var updateExisting = Builders<WorkflowInstanceChangeSet>.Update
                // .Set("PropertyChanges.$.OldValue", change.OldValue)
                .Set("PropertyChanges.$.NewValue", change.NewValue)
                .Set("PropertyChanges.$.Timestamp", change.Timestamp)
                .Set("PropertyChanges.$.ModifiedBy", change.ModifiedBy);

            var updateResult = await _changeSetCollection.UpdateOneAsync(
                matchExistingFilter,
                updateExisting,
                new UpdateOptions { IsUpsert = false },
                ct);

            // If nothing matched, append a new entry
            if (updateResult.ModifiedCount == 0)
            {
                var pushChangeUpdate = Builders<WorkflowInstanceChangeSet>.Update.Push(x => x.PropertyChanges, change);

                await _changeSetCollection.UpdateOneAsync(
                    instanceIdFilter,
                    pushChangeUpdate,
                    new UpdateOptions { IsUpsert = false },
                    ct);
            }
        }
    }

    public async Task<int> IncrementVersion(string instanceId, CancellationToken ct = default)
    {
        var instanceIdFilter = Builders<WorkflowInstanceChangeSet>.Filter.Eq(x => x.InstanceId, instanceId);
        var incrementVersionUpdate = Builders<WorkflowInstanceChangeSet>.Update.Inc(x => x.CurrentVersion, 1);

        var updated = await _changeSetCollection.FindOneAndUpdateAsync(
            instanceIdFilter,
            incrementVersionUpdate,
            new FindOneAndUpdateOptions<WorkflowInstanceChangeSet>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            ct);

        if (updated == null)
            throw new EntityNotFoundException("WorkflowInstanceChangeSet", $"instanceId:{instanceId}");

        return updated.CurrentVersion;
    }
}