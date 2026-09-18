using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Persistence.Mongo;

public class InstanceJournalService(IMongoDatabase db) : IInstanceJournalService
{
    private static readonly TimeSpan PropertyChangeMergeWindow = TimeSpan.FromMinutes(5);

    private readonly IMongoCollection<InstanceJournalEntry> _changeSetCollection =
        db.GetCollection<InstanceJournalEntry>("instance_journal");

    private readonly IMongoCollection<InstanceEventLogEntry> _eventLogCollection =
        db.GetCollection<InstanceEventLogEntry>("eventlog");

    public async Task<InstanceJournalEntry?> GetInstanceJournal(string instanceId, bool createIfNotExist = false,
        CancellationToken ct = default)
    {
        var filter = Builders<InstanceJournalEntry>.Filter.Eq(x => x.InstanceId, instanceId);
        var changeSet = await _changeSetCollection.Find(filter).FirstOrDefaultAsync(ct);
        if (changeSet == null && createIfNotExist)
        {
            changeSet = new InstanceJournalEntry
            {
                InstanceId = instanceId,
                CurrentVersion = 0,
            };
        }

        return changeSet!;
    }

    public async Task<bool> LogPropertyChange(string instanceId, PropertyChangeEntry valueChange,
        CancellationToken ct = default)
    {
        var result = await LogPropertyChanges(instanceId, [valueChange], ct);
        return result.Count > 0;
    }

    private async Task<ICollection<string>> LogPropertyChanges(string instanceId,
        ICollection<PropertyChangeEntry> newChanges,
        CancellationToken ct = default)
    {
        var changeSet = await GetInstanceJournal(instanceId, createIfNotExist: true, ct);

        foreach (var change in newChanges)
            change.Version = changeSet!.CurrentVersion;

        var instanceIdFilter = Builders<InstanceJournalEntry>.Filter.Eq(x => x.InstanceId, instanceId);

        var overwritten = new List<string>();

        foreach (var change in newChanges)
        {
            UpdateResult? updateResult = null;
            // Dated old values are immutable: keep every date change and its original timestamp.
            // Empty reinitialization entries must not merge into a dated entry either.
            if (change.OldValue is not BsonDateTime)
            {
                var cutoff = change.Timestamp.Subtract(PropertyChangeMergeWindow);
                var matchChange = Builders<PropertyChangeEntry>.Filter.Where(pc =>
                    pc.Version == change.Version && pc.Path == change.Path && pc.Timestamp >= cutoff);
                matchChange &= Builders<PropertyChangeEntry>.Filter.Not(
                    Builders<PropertyChangeEntry>.Filter.Type(pc => pc.OldValue, BsonType.DateTime));
                var matchExistingFilter = instanceIdFilter &
                                          Builders<InstanceJournalEntry>.Filter.ElemMatch(x => x.PropertyChanges,
                                              matchChange);
                var updateExisting = Builders<InstanceJournalEntry>.Update
                    .Set("PropertyChanges.$.Timestamp", change.Timestamp)
                    .Set("PropertyChanges.$.Reason", change.Reason)
                    .Set("PropertyChanges.$.ModifiedBy", change.ModifiedBy);
                updateResult = await _changeSetCollection.UpdateOneAsync(
                    matchExistingFilter, updateExisting, new UpdateOptions { IsUpsert = false }, ct);
            }

            if (updateResult?.MatchedCount is not > 0)
            {
                var pushChangeUpdate = Builders<InstanceJournalEntry>.Update
                    .Push(x => x.PropertyChanges, change)
                    .SetOnInsert(x => x.CurrentVersion, change.Version);

                await _changeSetCollection.UpdateOneAsync(
                    instanceIdFilter,
                    pushChangeUpdate,
                    new UpdateOptions { IsUpsert = true },
                    ct);
            }
            else
                overwritten.Add(change.Path);
        }

        return overwritten;
    }

    public async Task<int> IncrementVersion(string instanceId, CancellationToken ct = default)
    {
        var instanceIdFilter = Builders<InstanceJournalEntry>.Filter.Eq(x => x.InstanceId, instanceId);
        var incrementVersionUpdate = Builders<InstanceJournalEntry>.Update.Inc(x => x.CurrentVersion, 1);

        var updated = await _changeSetCollection.FindOneAndUpdateAsync(
            instanceIdFilter,
            incrementVersionUpdate,
            new FindOneAndUpdateOptions<InstanceJournalEntry>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            ct);

        if (updated == null)
            throw new EntityNotFoundException("InstanceJournalEntry", $"instanceId:{instanceId}");

        return updated.CurrentVersion;
    }
}