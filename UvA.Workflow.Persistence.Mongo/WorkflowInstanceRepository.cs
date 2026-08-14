using System.Linq.Expressions;
using UvA.Workflow.Migrations;

namespace UvA.Workflow.Persistence.Mongo;

/// <summary>
/// MongoDB implementation of the IWorkflowInstanceRepository contract.
/// Handles mapping between domain entities and MongoDB documents.
/// </summary>
public class WorkflowInstanceRepository(
    IMongoDatabase database,
    MigrationCompatibilityService? migrationCompatibility = null) : IWorkflowInstanceRepository
{
    private readonly IMongoCollection<WorkflowInstance> instanceCollection =
        database.GetCollection<WorkflowInstance>("instances");

    public async Task Create(WorkflowInstance instance, CancellationToken ct)
    {
        await AttachCompatibility(instance, ct);
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
        if (instance != null)
            await AttachCompatibility(instance, ct);
        return instance;
    }

    public async Task Update(WorkflowInstance instance, CancellationToken ct)
    {
        if (!ObjectId.TryParse(instance.Id, out var objectId))
            throw new ArgumentException("Invalid instance ID", nameof(instance.Id));

        await AttachCompatibility(instance, ct);
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
        await AttachCompatibility(documents, ct);
        return documents;
    }

    public async Task<IEnumerable<WorkflowInstance>> GetByWorkflowDefinition(string workflowDefinition,
        FilterDefinition<WorkflowInstance> filter,
        CancellationToken ct)
    {
        var combinedFilter = Builders<WorkflowInstance>.Filter.And(
            Builders<WorkflowInstance>.Filter.Eq(x => x.WorkflowDefinition, workflowDefinition),
            filter
        );
        var documents = await instanceCollection.Find(combinedFilter).ToListAsync(ct);
        await AttachCompatibility(documents, ct);
        return documents;
    }

    public async Task<IEnumerable<WorkflowInstance>> GetByFilter(FilterDefinition<WorkflowInstance> filter,
        CancellationToken ct)
    {
        var documents = await instanceCollection.Find(filter).ToListAsync(ct);
        await AttachCompatibility(documents, ct);
        return documents;
    }

    public async Task<IEnumerable<WorkflowInstance>> GetByParentId(string parentId, CancellationToken ct)
    {
        var filter = Builders<WorkflowInstance>.Filter.Eq(x => x.ParentId, parentId);
        var documents = await instanceCollection.Find(filter).ToListAsync(ct);
        await AttachCompatibility(documents, ct);
        return documents;
    }

    public async Task<List<WorkflowInstance>> GetAll(Expression<Func<WorkflowInstance, bool>> expression,
        CancellationToken ct)
    {
        var documents = await instanceCollection.Find(expression).ToListAsync(ct);
        await AttachCompatibility(documents, ct);
        return documents;
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
        => await GetAllByType(workflowDefinition, projection, null, ct);

    public async Task<List<Dictionary<string, BsonValue>>> GetAllByType(string workflowDefinition,
        Dictionary<string, string> projection, BsonDocument? authorizationFilter, CancellationToken ct)
    {
        var matchFilter = authorizationFilter != null
            ? new BsonDocument("$and", new BsonArray
            {
                new BsonDocument { ["WorkflowDefinition"] = workflowDefinition },
                authorizationFilter
            })
            : new BsonDocument { ["WorkflowDefinition"] = workflowDefinition };

        // An empty $project is invalid in MongoDB; when no properties are requested, return just the
        // identifier so callers can still list instances of a type.
        var projectionDoc = projection.Count == 0
            ? new BsonDocument { ["_id"] = 1 }
            : projection.ToBsonDocument();

        BsonDocument[] pipeline =
        [
            new("$match", matchFilter),
            new("$project", projectionDoc)
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

    public async Task UpdateArrayFields(string instanceId, UpdateDefinition<WorkflowInstance> updateDefinition,
        IEnumerable<ArrayFilterDefinition> arrayFilters, CancellationToken ct)
    {
        if (!ObjectId.TryParse(instanceId, out var objectId))
            throw new ArgumentException("Invalid instance ID", nameof(instanceId));

        var filter = Builders<WorkflowInstance>.Filter.Eq("_id", objectId);
        var options = new UpdateOptions { ArrayFilters = arrayFilters.ToList() };
        await instanceCollection.UpdateOneAsync(filter, updateDefinition, options, cancellationToken: ct);
    }

    private async Task AttachCompatibility(WorkflowInstance instance, CancellationToken ct)
    {
        if (migrationCompatibility != null)
            await migrationCompatibility.Attach(instance, ct);
    }

    private async Task AttachCompatibility(IEnumerable<WorkflowInstance> instances, CancellationToken ct)
    {
        if (migrationCompatibility != null)
            await migrationCompatibility.Attach(instances, ct);
    }
}