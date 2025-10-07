using System.Linq.Expressions;

namespace Uva.Workflow.WorkflowInstances;

/// <summary>
/// MongoDB implementation of the IWorkflowInstanceRepository contract.
/// Handles mapping between domain entities and MongoDB documents.
/// </summary>
public class WorkflowInstanceRepository(IMongoDatabase database) : IWorkflowInstanceRepository
{
    private readonly IMongoCollection<WorkflowInstance> _collection =
        database.GetCollection<WorkflowInstance>("instances");

    public async Task CreateAsync(WorkflowInstance instance)
    {
        var document = instance;
        await _collection.InsertOneAsync(document);
        instance.Id = document.Id; // Update with generated ID
    }

    public async Task<WorkflowInstance?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        var instance = await _collection.Find(filter).FirstOrDefaultAsync();
        return instance;
    }

    public async Task UpdateAsync(WorkflowInstance instance)
    {
        if (!ObjectId.TryParse(instance.Id, out var objectId))
            throw new ArgumentException("Invalid instance ID", nameof(instance.Id));

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        await _collection.ReplaceOneAsync(filter, instance);
    }

    public async Task DeleteAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return;

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        await _collection.DeleteOneAsync(filter);
    }

    public async Task<IEnumerable<WorkflowInstance>> GetByIdsAsync(IEnumerable<string> ids)
    {
        var objectIds = ids
            .Select(id => ObjectId.TryParse(id, out var oid) ? oid : (ObjectId?)null)
            .Where(oid => oid.HasValue)
            .Select(oid => oid!.Value)
            .ToList();

        var filter = Builders<WorkflowInstance>.Filter.In("_id", objectIds);
        var documents = await _collection.Find(filter).ToListAsync();
        return documents;
    }

    public async Task<IEnumerable<WorkflowInstance>> GetByEntityTypeAsync(string entityType)
    {
        var filter = Builders<WorkflowInstance>.Filter.Eq(x => x.EntityType, entityType);
        var documents = await _collection.Find(filter).ToListAsync();
        return documents;
    }

    public async Task<IEnumerable<WorkflowInstance>> GetByParentIdAsync(string parentId)
    {
        var filter = Builders<WorkflowInstance>.Filter.Eq(x => x.ParentId, parentId);
        var documents = await _collection.Find(filter).ToListAsync();
        return documents;
    }

    public async Task<List<WorkflowInstance>> GetAllAsync(Expression<Func<WorkflowInstance, bool>> expression)
    {
        return await _collection.Find(expression).ToListAsync();
    }

    public async Task<T?> GetAsync<T>(string instanceId, Expression<Func<WorkflowInstance, T>> expression)
    {
        var projection = Builders<WorkflowInstance>.Projection.Expression(expression);
        var filter = Builders<WorkflowInstance>.Filter.Eq(p => p.Id, instanceId);
        return await _collection.Find(filter).Project(projection).FirstOrDefaultAsync();
    }

    public async Task<T?> GetAsync<T>(Expression<Func<WorkflowInstance, bool>> predicate,
        Expression<Func<WorkflowInstance, T>> project)
    {
        var projection = Builders<WorkflowInstance>.Projection.Expression(project);
        var filter = Builders<WorkflowInstance>.Filter.Where(predicate);
        return await _collection.Find(filter).Project(projection).FirstOrDefaultAsync();
    }

    public async Task<List<Dictionary<string, BsonValue>>> GetAllByTypeAsync(string entityType,
        Dictionary<string, string> projection)
    {
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument { ["EntityType"] = entityType }),
            new("$project", projection.ToBsonDocument())
        ];

        return await _collection.Aggregate<Dictionary<string, BsonValue>>(pipeline).ToListAsync();
    }

    public async Task<List<Dictionary<string, BsonValue>>> GetAllByParentIdAsync(string parentId,
        Dictionary<string, string> projection)
    {
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument { ["ParentId"] = parentId }),
            new("$project", projection.ToBsonDocument())
        ];

        return await _collection.Aggregate<Dictionary<string, BsonValue>>(pipeline).ToListAsync();
    }

    public async Task<List<Dictionary<string, BsonValue>>> GetAllByIdAsync(string[] ids,
        Dictionary<string, string> projection)
    {
        BsonDocument[] pipeline =
        [
            new("$match", new BsonDocument("_id",
                new BsonDocument { ["$in"] = new BsonArray(ids.Select(i => new ObjectId(i))) })),
            new("$project", projection.ToBsonDocument())
        ];

        return await _collection.Aggregate<Dictionary<string, BsonValue>>(pipeline).ToListAsync();
    }

    public async Task UpdateFieldAsync<TField>(string instanceId, Expression<Func<WorkflowInstance, TField>> field,
        TField value)
    {
        if (!ObjectId.TryParse(instanceId, out var objectId))
            throw new ArgumentException("Invalid instance ID", nameof(instanceId));

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        var update = Builders<WorkflowInstance>.Update.Set(field, value);

        await _collection.UpdateOneAsync(filter, update);
    }

    public async Task UpdateFieldsAsync(string instanceId, UpdateDefinition<WorkflowInstance> updateDefinition)
    {
        if (!ObjectId.TryParse(instanceId, out var objectId))
            throw new ArgumentException("Invalid instance ID", nameof(instanceId));

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        await _collection.UpdateOneAsync(filter, updateDefinition);
    }
}