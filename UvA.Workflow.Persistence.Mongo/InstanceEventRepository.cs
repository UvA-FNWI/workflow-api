using MongoDB.Bson.Serialization;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Persistence.Mongo;

public class InstanceEventRepository(IMongoDatabase database) : IInstanceEventRepository
{
    private readonly IMongoCollection<InstanceEventLogEntry> _eventLogCollection =
        database.GetCollection<InstanceEventLogEntry>("eventlog");

    private readonly IMongoCollection<WorkflowInstance> _instanceCollection =
        database.GetCollection<WorkflowInstance>("instances");

    /// <summary>
    /// Adds a new event to a workflow instance or updates an existing event if it already exists.
    /// Logs the operation specifying whether it was an addition or update.
    /// </summary>
    /// <param name="instance">The workflow instance in which the event should be added or updated.</param>
    /// <param name="newEvent">The new event to add or the existing event to update.</param>
    /// <param name="user">The user initiating the add or update operation.</param>
    /// <param name="ct">The cancellation token used to observe the operation's cancellation.</param>
    /// <returns>An asynchronous operation representing the add or update process.</returns>
    public async Task AddOrUpdateEvent(WorkflowInstance instance, InstanceEvent newEvent, User user,
        CancellationToken ct, string? operationId = null, OperationMetadata? operationMetadata = null)
    {
        // Add or update existing event in the instance
        var filter = Builders<WorkflowInstance>.Filter.Eq(i => i.Id, instance.Id);
        var update = Builders<WorkflowInstance>.Update
            .Set(i => i.Events[newEvent.Id], newEvent);

        // Use FindOneAndUpdate to apply the change AND return the state Before the change
        var options = new FindOneAndUpdateOptions<WorkflowInstance>
        {
            ReturnDocument = ReturnDocument.Before,
            // Optional: Optimize by only retrieving the Events field
            Projection = Builders<WorkflowInstance>.Projection.Include(i => i.Events)
        };

        var originalDoc =
            await _instanceCollection.FindOneAndUpdateAsync(filter, update, options, cancellationToken: ct);

        // Determine the event-log entry type by checking if the key existed previously
        var wasUpdated = originalDoc != null &&
                         originalDoc.Events.ContainsKey(newEvent.Id);

        // Also add the event to the event log collection
        await AddEventLogEntry(instance, newEvent, user,
            wasUpdated ? EventLogOperation.Update : EventLogOperation.Create, ct, operationId, operationMetadata);
    }

    public async Task RecordRestartEvent(WorkflowInstance instance, WorkflowDefinition definition,
        string eventId, User user, CancellationToken ct, string? operationId = null,
        OperationMetadata? operationMetadata = null)
    {
        var filters = Builders<WorkflowInstance>.Filter;
        var instanceFilter = filters.Eq(item => item.Id, instance.Id);
        var projection = Builders<WorkflowInstance>.Projection.Include(item => item.Events);
        while (true)
        {
            var persisted = await _instanceCollection.Find(instanceFilter).Project<BsonDocument>(projection)
                                .FirstOrDefaultAsync(ct)
                            ?? throw new EntityNotFoundException(nameof(WorkflowInstance), instance.Id);
            var current = BsonSerializer.Deserialize<WorkflowInstance>(persisted);
            current.Events ??= [];
            var operation = current.Events.ContainsKey(eventId) ? EventLogOperation.Update : EventLogOperation.Create;
            var recorded = ParentStepPasses.RecordEvent(current, definition, eventId);

            // Another writer may have completed a different prerequisite since we read the instance.
            // Match the stored BSON so older field ordering and omitted values still compare correctly.
            const string eventsField = nameof(WorkflowInstance.Events);
            FilterDefinition<WorkflowInstance> unchangedEvents =
                persisted.TryGetValue(eventsField, out var originalEvents)
                    ? new BsonDocument(eventsField, new BsonDocument("$eq", originalEvents))
                    : filters.Exists(item => item.Events, false);
            var update = Builders<WorkflowInstance>.Update.Set(item => item.Events, current.Events);
            var result = await _instanceCollection.UpdateOneAsync(instanceFilter & unchangedEvents, update,
                cancellationToken: ct);
            if (result.MatchedCount == 0)
                continue;

            instance.Events = current.Events;
            var source = recorded[0];
            // Preserve the committed order if another writer inserts its journal row first.
            await AddEventLogEntry(instance, source, user, operation, ct, operationId,
                operationMetadata, recorded.Length > 1 ? recorded[1..] : null, source.Date);
            return;
        }
    }

    /// <summary>
    /// Deletes a specified event from a workflow instance and logs the deletion.
    /// </summary>
    /// <param name="instance">The workflow instance from which the event is to be deleted.</param>
    /// <param name="eventToDelete">The event to remove from the instance.</param>
    /// <param name="user">The user executing the deletion action.</param>
    /// <param name="ct">The cancellation token used to observe the operation's cancellation.</param>
    /// <returns>An asynchronous operation representing the deletion process.</returns>
    public async Task DeleteEvent(WorkflowInstance instance, InstanceEvent eventToDelete, User user,
        CancellationToken ct)
    {
        var filter = Builders<WorkflowInstance>.Filter.Eq(i => i.Id, instance.Id);

        var delete = Builders<WorkflowInstance>.Update
            .Unset(i => i.Events[eventToDelete.Id]);

        var options = new FindOneAndUpdateOptions<WorkflowInstance>
        {
            ReturnDocument = ReturnDocument.Before,
            Projection = Builders<WorkflowInstance>.Projection.Include(i => i.Events)
        };

        // Perform atomic delete and retrieve the instance state BEFORE the delete
        var originalDoc =
            await _instanceCollection.FindOneAndUpdateAsync(filter, delete, options, cancellationToken: ct);

        // Verify the instance and event existed before logging
        if (originalDoc != null && originalDoc.Events.ContainsKey(eventToDelete.Id))
        {
            // Also add the deletion of the event to the event log collection
            await AddEventLogEntry(instance, eventToDelete, user, EventLogOperation.Delete, ct);
        }
    }

    /// <summary>
    /// Counts the number of event log entries for a specified instance and event.
    /// </summary>
    /// <param name="instanceId">The identifier of the workflow instance to filter the event logs by.</param>
    /// <param name="eventId">The identifier of the event to filter the event logs by.</param>
    /// <param name="ct">The cancellation token used to observe the operation's cancellation.</param>
    /// <returns>The total count of event log entries that match the specified instance ID and event ID.</returns>
    public async Task<long> CountEventLogFor(string instanceId, string eventId, CancellationToken ct)
    {
        var filter = Builders<InstanceEventLogEntry>.Filter.And(
            Builders<InstanceEventLogEntry>.Filter.Eq(x => x.WorkflowInstanceId, instanceId),
            Builders<InstanceEventLogEntry>.Filter.Eq(x => x.EventId, eventId));
        return await _eventLogCollection.CountDocumentsAsync(filter, cancellationToken: ct);
    }


    /// <summary>
    /// Adds an event log entry to the event log collection
    /// </summary>
    public Task AddEventLogEntry(WorkflowInstance instance, InstanceEvent instanceEvent, User user,
        EventLogOperation type, CancellationToken ct, string? operationId = null,
        OperationMetadata? operationMetadata = null)
        => AddEventLogEntry(instance, instanceEvent, user, type, ct, operationId, operationMetadata, null);

    private async Task AddEventLogEntry(WorkflowInstance instance, InstanceEvent instanceEvent, User user,
        EventLogOperation type, CancellationToken ct, string? operationId, OperationMetadata? operationMetadata,
        InstanceEvent[]? passBoundaries, DateTime? timestamp = null)
    {
        var logEntry = new InstanceEventLogEntry
        {
            Timestamp = timestamp ?? DateTime.UtcNow,
            WorkflowInstanceId = instance.Id,
            EventId = instanceEvent.Id,
            EventDate = instanceEvent.Date,
            Operation = type,
            ExecutedBy = user.Id,
            OperationId = operationMetadata?.Id ?? operationId,
            PassBoundaries = passBoundaries
        };

        // Store metadata once; later changes reference the same operation by ID.
        if (operationMetadata != null && await _eventLogCollection
                .Find(entry => entry.Id == operationMetadata.Id)
                .FirstOrDefaultAsync(ct) == null)
        {
            logEntry.Id = operationMetadata.Id;
            logEntry.OperationMetadata = operationMetadata;
        }

        await _eventLogCollection.InsertOneAsync(logEntry, cancellationToken: ct);

        // A late consequence of an undone operation must not become effective again.
        if (logEntry.OperationId == null || await _eventLogCollection.CountDocumentsAsync(
                entry => entry.Operation == EventLogOperation.Undo && entry.OperationId == logEntry.OperationId,
                new CountOptions { Limit = 1 }, ct) == 0)
            return;

        var effectiveEvents = EventHistory.RebuildEvents(await GetEventLogEntriesForInstance(instance.Id, ct));
        var updates = Builders<WorkflowInstance>.Update;
        var repairs = new List<UpdateDefinition<WorkflowInstance>>();
        foreach (var recorded in (passBoundaries ?? []).Prepend(instanceEvent))
        {
            var eventId = recorded.Id;
            if (effectiveEvents.TryGetValue(eventId, out var effectiveEvent))
            {
                instance.Events[eventId] = effectiveEvent;
                repairs.Add(updates.Set(entry => entry.Events[eventId], effectiveEvent));
            }
            else
            {
                instance.Events.Remove(eventId);
                repairs.Add(updates.Unset(entry => entry.Events[eventId]));
            }
        }

        await _instanceCollection.UpdateOneAsync(
            entry => entry.Id == instance.Id,
            updates.Combine(repairs),
            cancellationToken: ct);
    }

    public Task AddUndoEntry(string instanceId, string operationId, User user, string reason,
        CancellationToken ct)
        => _eventLogCollection.InsertOneAsync(new InstanceEventLogEntry
        {
            WorkflowInstanceId = instanceId,
            ExecutedBy = user.Id,
            Operation = EventLogOperation.Undo,
            OperationId = operationId,
            Reason = reason
        }, cancellationToken: ct);

    /// <summary>
    /// Gets all event log entries for specific events in an instance
    /// </summary>
    public async Task<List<InstanceEventLogEntry>> GetEventLogEntriesForInstance(
        string instanceId,
        List<string> eventIds,
        CancellationToken ct)
    {
        var filter = Builders<InstanceEventLogEntry>.Filter.And(
            Builders<InstanceEventLogEntry>.Filter.Eq(e => e.WorkflowInstanceId, instanceId),
            Builders<InstanceEventLogEntry>.Filter.In(e => e.EventId, eventIds)
        );

        return await _eventLogCollection
            .Find(filter)
            .SortBy(e => e.Timestamp)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets all event log entries for an instance.
    /// </summary>
    public async Task<List<InstanceEventLogEntry>> GetEventLogEntriesForInstance(
        string instanceId,
        CancellationToken ct)
    {
        var filter = Builders<InstanceEventLogEntry>.Filter.Eq(e => e.WorkflowInstanceId, instanceId);

        return await _eventLogCollection
            .Find(filter)
            .SortBy(e => e.Timestamp)
            .ToListAsync(ct);
    }
}