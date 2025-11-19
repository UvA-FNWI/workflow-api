using System.Linq.Expressions;

namespace UvA.Workflow.WorkflowInstances;

/// <summary>
/// MongoDB implementation of the IWorkflowInstanceRepository contract.
/// Handles mapping between domain entities and MongoDB documents.
/// </summary>
public class WorkflowInstanceRepository(IMongoDatabase database) : IWorkflowInstanceRepository
{
    private readonly IMongoCollection<WorkflowInstance> instanceCollection =
        database.GetCollection<WorkflowInstance>("instances");

    private readonly IMongoCollection<InstanceEventLogEntry> eventLogCollection =
        database.GetCollection<InstanceEventLogEntry>("eventlog");

    public async Task Create(WorkflowInstance instance, CancellationToken ct)
    {
        var document = instance;
        await instanceCollection.InsertOneAsync(document, cancellationToken: ct);
        instance.Id = document.Id; // Update with generated ID
    }

    public async Task<WorkflowInstance?> GetById(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        var instance = await instanceCollection.Find(filter).FirstOrDefaultAsync(ct);
        return instance;
    }

    public async Task Update(WorkflowInstance instance, CancellationToken ct)
    {
        if (!ObjectId.TryParse(instance.Id, out var objectId))
            throw new ArgumentException("Invalid instance ID", nameof(instance.Id));

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        await instanceCollection.ReplaceOneAsync(filter, instance, cancellationToken: ct);
    }

    public async Task Delete(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return;

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        await instanceCollection.DeleteOneAsync(filter, ct);
    }

    public async Task<IEnumerable<WorkflowInstance>> GetByIds(IEnumerable<string> ids, CancellationToken ct)
    {
        var objectIds = ids
            .Select(id => ObjectId.TryParse(id, out var oid) ? oid : (ObjectId?)null)
            .Where(oid => oid.HasValue)
            .Select(oid => oid!.Value)
            .ToList();

        var filter = Builders<WorkflowInstance>.Filter.In("_id", objectIds);
        var documents = await instanceCollection.Find(filter).ToListAsync(ct);
        return documents;
    }

    public async Task<IEnumerable<WorkflowInstance>> GetByEntityType(string entityType, CancellationToken ct)
    {
        var filter = Builders<WorkflowInstance>.Filter.Eq(x => x.EntityType, entityType);
        var documents = await instanceCollection.Find(filter).ToListAsync(ct);
        return documents;
    }

    public async Task<IEnumerable<WorkflowInstance>> GetByParentId(string parentId, CancellationToken ct)
    {
        var filter = Builders<WorkflowInstance>.Filter.Eq(x => x.ParentId, parentId);
        var documents = await instanceCollection.Find(filter).ToListAsync(ct);
        return documents;
    }

    public async Task<List<WorkflowInstance>> GetAll(Expression<Func<WorkflowInstance, bool>> expression,
        CancellationToken ct)
    {
        return await instanceCollection.Find(expression).ToListAsync(ct);
    }

    public async Task<T?> Get<T>(string instanceId, Expression<Func<WorkflowInstance, T>> expression,
        CancellationToken ct)
    {
        var projection = Builders<WorkflowInstance>.Projection.Expression(expression);
        var filter = Builders<WorkflowInstance>.Filter.Eq(p => p.Id, instanceId);
        return await instanceCollection.Find(filter).Project(projection).FirstOrDefaultAsync(ct);
    }

    public async Task<T?> Get<T>(Expression<Func<WorkflowInstance, bool>> predicate,
        Expression<Func<WorkflowInstance, T>> project, CancellationToken ct)
    {
        var projection = Builders<WorkflowInstance>.Projection.Expression(project);
        var filter = Builders<WorkflowInstance>.Filter.Where(predicate);
        return await instanceCollection.Find(filter).Project(projection).FirstOrDefaultAsync(ct);
    }

    public async Task<List<Dictionary<string, BsonValue>>> GetAllByType(string entityType,
        Dictionary<string, string> projection, CancellationToken ct)
    {
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument { ["EntityType"] = entityType }),
            new("$project", projection.ToBsonDocument())
        ];

        return await instanceCollection.Aggregate<Dictionary<string, BsonValue>>(pipeline).ToListAsync(ct);
    }

    public async Task<List<Dictionary<string, BsonValue>>> GetAllByParentId(string parentId,
        Dictionary<string, string> projection, CancellationToken ct)
    {
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument { ["ParentId"] = parentId }),
            new("$project", projection.ToBsonDocument())
        ];

        return await instanceCollection.Aggregate<Dictionary<string, BsonValue>>(pipeline).ToListAsync(ct);
    }

    public async Task<List<Dictionary<string, BsonValue>>> GetAllById(string[] ids,
        Dictionary<string, string> projection, CancellationToken ct)
    {
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument("_id",
                new BsonDocument { ["$in"] = new BsonArray(ids.Select(i => new ObjectId(i))) })),
            new("$project", projection.ToBsonDocument())
        ];

        return await instanceCollection.Aggregate<Dictionary<string, BsonValue>>(pipeline).ToListAsync(ct);
    }

    public async Task UpdateField<TField>(string instanceId, Expression<Func<WorkflowInstance, TField>> field,
        TField value, CancellationToken ct)
    {
        if (!ObjectId.TryParse(instanceId, out var objectId))
            throw new ArgumentException("Invalid instance ID", nameof(instanceId));

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        var update = Builders<WorkflowInstance>.Update.Set(field, value);

        await instanceCollection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task DeleteField(string instanceId, Expression<Func<WorkflowInstance, object>> field,
        CancellationToken ct)
    {
        if (!ObjectId.TryParse(instanceId, out var objectId))
            throw new ArgumentException("Invalid instance ID", nameof(instanceId));

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        var update = Builders<WorkflowInstance>.Update.Unset(field);

        await instanceCollection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task UpdateFields(string instanceId, UpdateDefinition<WorkflowInstance> updateDefinition,
        CancellationToken ct)
    {
        if (!ObjectId.TryParse(instanceId, out var objectId))
            throw new ArgumentException("Invalid instance ID", nameof(instanceId));

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        await instanceCollection.UpdateOneAsync(filter, updateDefinition, cancellationToken: ct);
    }

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
            await instanceCollection.FindOneAndUpdateAsync(filter, update, options, cancellationToken: ct);

        // Determine operation type by checking if the key existed previously
        var wasUpdated = originalDoc != null &&
                         originalDoc.Events.ContainsKey(newEvent.Id);

        // Also add the event to the event log collection
        await AddEventLogEntry(instance, newEvent, user, wasUpdated ? "update" : "create", ct);
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
            await instanceCollection.FindOneAndUpdateAsync(filter, delete, options, cancellationToken: ct);

        // Verify the instance and event existed before logging
        if (originalDoc != null && originalDoc.Events.ContainsKey(eventToDelete.Id))
        {
            // Also add the deletion of the event to the event log collection
            await AddEventLogEntry(instance, eventToDelete, user, "delete", ct);
        }
    }

    private async Task AddEventLogEntry(WorkflowInstance instance, InstanceEvent instanceEvent, User user,
        string operation, CancellationToken ct)
    {
        var logEntry = new InstanceEventLogEntry
        {
            Timestamp = DateTime.UtcNow,
            WorkflowInstanceId = instance.Id,
            EventId = instanceEvent.Id,
            EventDate = instanceEvent.Date,
            Operation = operation,
            ExecutedBy = user.Id
        };
        await eventLogCollection.InsertOneAsync(logEntry, cancellationToken: ct);
    }
}