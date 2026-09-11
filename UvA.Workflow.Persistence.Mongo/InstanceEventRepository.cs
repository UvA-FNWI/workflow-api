namespace UvA.Workflow.Persistence.Mongo;

public class InstanceEventRepository(IMongoDatabase database) : IInstanceEventRepository
{
    private readonly IMongoCollection<InstanceEventLogEntry> _eventLogCollection =
        database.GetCollection<InstanceEventLogEntry>("eventlog");

    private readonly IMongoCollection<WorkflowInstance> _instanceCollection =
        database.GetCollection<WorkflowInstance>("instances");

    private readonly IMongoCollection<Job> _jobCollection =
        database.GetCollection<Job>("jobs");

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
        CancellationToken ct)
        => await AddOrUpdateEvent(instance, newEvent, user, null, ct);

    public async Task AddOrUpdateEvent(WorkflowInstance instance, InstanceEvent newEvent, User user,
        OperationMetadata? operation, CancellationToken ct)
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

        // Determine operation type by checking if the key existed previously
        var wasUpdated = originalDoc != null &&
                         originalDoc.Events.ContainsKey(newEvent.Id);

        // Also add the event to the event log collection
        await AddEventLogEntry(instance, newEvent, user,
            wasUpdated ? EventLogOperation.Update : EventLogOperation.Create, operation, ct);
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
    public async Task AddEventLogEntry(WorkflowInstance instance, InstanceEvent instanceEvent, User user,
        EventLogOperation operation, CancellationToken ct)
        => await AddEventLogEntry(instance, instanceEvent, user, operation, null, ct);

    public async Task AddEventLogEntry(WorkflowInstance instance, InstanceEvent instanceEvent, User user,
        EventLogOperation operation, OperationMetadata? operationMetadata, CancellationToken ct)
    {
        var logEntry = new InstanceEventLogEntry
        {
            Timestamp = DateTime.UtcNow,
            WorkflowInstanceId = instance.Id,
            EventId = instanceEvent.Id,
            EventDate = instanceEvent.Date,
            Operation = operation,
            ExecutedBy = user.Id,
            OperationId = operationMetadata?.Id
        };

        if (operationMetadata == null)
        {
            await _eventLogCollection.InsertOneAsync(logEntry, cancellationToken: ct);
            return;
        }

        var existingRoot = await _eventLogCollection
            .Find(entry => entry.Id == operationMetadata.Id)
            .FirstOrDefaultAsync(ct);
        if (existingRoot != null)
        {
            await _eventLogCollection.InsertOneAsync(logEntry, cancellationToken: ct);
            return;
        }

        while (true)
        {
            var historyEntries = await _eventLogCollection
                .Find(entry => entry.WorkflowInstanceId == instance.Id &&
                               (entry.OperationMetadata!.TopLevelStep == operationMetadata.TopLevelStep ||
                                entry.UndoMetadata!.TopLevelStep == operationMetadata.TopLevelStep))
                .ToListAsync(ct);
            var latestRevision = LatestHistoryRevision(historyEntries);

            logEntry.Id = operationMetadata.Id;
            logEntry.OperationMetadata = operationMetadata with { Revision = latestRevision + 1 };
            logEntry.HistoryTopLevelStep = operationMetadata.TopLevelStep;
            logEntry.HistoryRevision = latestRevision + 1;

            try
            {
                await _eventLogCollection.InsertOneAsync(logEntry, cancellationToken: ct);
                return;
            }
            catch (MongoWriteException exception) when (exception.WriteError.Category ==
                                                        ServerErrorCategory.DuplicateKey)
            {
                existingRoot = await _eventLogCollection
                    .Find(entry => entry.Id == operationMetadata.Id)
                    .FirstOrDefaultAsync(ct);
                if (existingRoot != null)
                {
                    logEntry.Id = null!;
                    logEntry.OperationMetadata = null;
                    await _eventLogCollection.InsertOneAsync(logEntry, cancellationToken: ct);
                    return;
                }
            }
        }
    }

    public async Task<bool> AddUndoEntry(string instanceId, string topLevelStep, string targetOperationId,
        long expectedRevision, User user, string reason, CancellationToken ct)
    {
        using var session = await database.Client.StartSessionAsync(cancellationToken: ct);
        session.StartTransaction();
        try
        {
            var roots = await _eventLogCollection
                .Find(session, entry => entry.WorkflowInstanceId == instanceId &&
                                        entry.OperationMetadata!.TopLevelStep == topLevelStep)
                .ToListAsync(ct);
            var undone = await _eventLogCollection
                .Find(session, entry => entry.WorkflowInstanceId == instanceId &&
                                        entry.Operation == EventLogOperation.Undo &&
                                        entry.UndoMetadata!.TopLevelStep == topLevelStep)
                .ToListAsync(ct);
            var candidate = EventHistory.LatestOperations(roots.Concat(undone))
                .GetValueOrDefault(topLevelStep);

            if (candidate?.Id != targetOperationId || candidate.Revision != expectedRevision)
            {
                await session.AbortTransactionAsync(ct);
                return false;
            }

            try
            {
                var nextHistoryRevision = LatestHistoryRevision(roots.Concat(undone)) + 1;
                await _eventLogCollection.InsertOneAsync(session, new InstanceEventLogEntry
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Timestamp = DateTime.UtcNow,
                    WorkflowInstanceId = instanceId,
                    EventId = targetOperationId,
                    ExecutedBy = user.Id,
                    Operation = EventLogOperation.Undo,
                    OperationId = targetOperationId,
                    HistoryTopLevelStep = topLevelStep,
                    HistoryRevision = nextHistoryRevision,
                    UndoMetadata = new UndoMetadata
                    {
                        TargetOperationId = targetOperationId,
                        TopLevelStep = topLevelStep,
                        Revision = expectedRevision,
                        Reason = reason
                    }
                }, cancellationToken: ct);
                await _jobCollection.UpdateManyAsync(session,
                    job => job.InstanceId == instanceId &&
                           job.Operation!.Id == targetOperationId &&
                           job.Status == JobStatus.Pending,
                    Builders<Job>.Update.Set(job => job.Status, JobStatus.Cancelled),
                    cancellationToken: ct);
            }
            catch (MongoWriteException exception) when (exception.WriteError.Category ==
                                                        ServerErrorCategory.DuplicateKey)
            {
                await session.AbortTransactionAsync(ct);
                return false;
            }

            await session.CommitTransactionAsync(ct);
            return true;
        }
        catch
        {
            if (session.IsInTransaction)
                await session.AbortTransactionAsync(ct);
            throw;
        }
    }

    private static long LatestHistoryRevision(IEnumerable<InstanceEventLogEntry> entries)
        => entries
            .Select(entry => entry.HistoryRevision ?? entry.OperationMetadata?.Revision ??
                entry.UndoMetadata?.Revision ?? 0)
            .DefaultIfEmpty()
            .Max();

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