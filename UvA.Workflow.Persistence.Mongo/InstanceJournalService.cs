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

        var mergedPaths = new List<string>();

        foreach (var change in newChanges)
        {
            // Dated old values are immutable: keep every date change and its original timestamp.
            if (change.OldValue is not BsonDateTime &&
                await TryMergePropertyChange(instanceIdFilter, change, ct))
            {
                mergedPaths.Add(change.Path);
                continue;
            }

            await AppendPropertyChange(instanceIdFilter, change, ct);
        }

        return mergedPaths;
    }

    private async Task<bool> TryMergePropertyChange(
        FilterDefinition<InstanceJournalEntry> instanceIdFilter,
        PropertyChangeEntry change,
        CancellationToken ct)
    {
        var cutoff = change.Timestamp.Subtract(PropertyChangeMergeWindow);
        var matchingChange = Builders<PropertyChangeEntry>.Filter.Where(existing =>
            existing.Version == change.Version &&
            existing.Path == change.Path &&
            existing.Timestamp >= cutoff);
        // Do not merge a reinitialization entry into a dated history entry.
        matchingChange &= Builders<PropertyChangeEntry>.Filter.Not(
            Builders<PropertyChangeEntry>.Filter.Type(existing => existing.OldValue, BsonType.DateTime));

        var matchingJournal = instanceIdFilter &
                              Builders<InstanceJournalEntry>.Filter.ElemMatch(
                                  entry => entry.PropertyChanges, matchingChange);
        var update = Builders<InstanceJournalEntry>.Update
            .Set("PropertyChanges.$.Timestamp", change.Timestamp)
            .Set("PropertyChanges.$.Reason", change.Reason)
            .Set("PropertyChanges.$.ModifiedBy", change.ModifiedBy);

        var result = await _changeSetCollection.UpdateOneAsync(
            matchingJournal, update, new UpdateOptions { IsUpsert = false }, ct);
        return result.MatchedCount > 0;
    }

    private async Task AppendPropertyChange(
        FilterDefinition<InstanceJournalEntry> instanceIdFilter,
        PropertyChangeEntry change,
        CancellationToken ct)
    {
        var update = Builders<InstanceJournalEntry>.Update
            .Push(entry => entry.PropertyChanges, change)
            .SetOnInsert(entry => entry.CurrentVersion, change.Version);

        await _changeSetCollection.UpdateOneAsync(
            instanceIdFilter, update, new UpdateOptions { IsUpsert = true }, ct);
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