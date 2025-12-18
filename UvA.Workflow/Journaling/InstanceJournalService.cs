using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Journaling;

public interface IInstanceJournalService
{
    Task<InstanceJournalEntry?> GetInstanceJournal(string instanceId, bool createIfNotExist = false,
        CancellationToken ct = default);

    Task LogPropertyChange(string instanceId, PropertyChangeEntry valueChange, CancellationToken ct = default);

    Task LogPropertyChanges(string instanceId, ICollection<PropertyChangeEntry> newChanges,
        CancellationToken ct = default);

    Task<int> IncrementVersion(string instanceId, CancellationToken ct = default);
}

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

    public async Task LogPropertyChange(string instanceId, PropertyChangeEntry valueChange,
        CancellationToken ct = default)
    {
        await LogPropertyChanges(instanceId, [valueChange], ct);
    }

    public async Task LogPropertyChanges(string instanceId, ICollection<PropertyChangeEntry> newChanges,
        CancellationToken ct = default)
    {
        var changeSet = await GetInstanceJournal(instanceId, createIfNotExist: true, ct);

        // Assign the current version to all changes we are logging
        foreach (var change in newChanges)
            change.Version = changeSet!.CurrentVersion;

        var instanceIdFilter = Builders<InstanceJournalEntry>.Filter.Eq(x => x.InstanceId, instanceId);

        foreach (var change in newChanges)
        {
            var cutoff = change.Timestamp.Subtract(PropertyChangeMergeWindow);

            var matchExistingFilter =
                instanceIdFilter &
                Builders<InstanceJournalEntry>.Filter.ElemMatch(
                    x => x.PropertyChanges,
                    pc => pc.Version == change.Version &&
                          pc.Path == change.Path &&
                          pc.Timestamp >= cutoff
                );

            // Update the matched array element in-place
            var updateExisting = Builders<InstanceJournalEntry>.Update
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
                var pushChangeUpdate = Builders<InstanceJournalEntry>.Update.Push(x => x.PropertyChanges, change);

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