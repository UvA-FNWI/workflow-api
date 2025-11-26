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

    public async Task<IEnumerable<WorkflowInstance>> GetByWorkflowDefinition(string workflowDefinition,
        CancellationToken ct)
    {
        var filter = Builders<WorkflowInstance>.Filter.Eq(x => x.WorkflowDefinition, workflowDefinition);
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

    public async Task<List<Dictionary<string, BsonValue>>> GetAllByType(string workflowDefinition,
        Dictionary<string, string> projection, CancellationToken ct)
    {
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument { ["WorkflowDefinition"] = workflowDefinition }),
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
}