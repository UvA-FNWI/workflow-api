using System.Linq.Expressions;

namespace Uva.Workflow.WorkflowInstances;

public class WorkflowInstanceService(IWorkflowInstanceRepository repository)
{
    /// <summary>
    /// Creates a new workflow instance
    /// </summary>
    public async Task<WorkflowInstance> CreateAsync(
        string entityType,
        string? variant = null,
        string? parentId = null,
        Dictionary<string, BsonValue>? initialProperties = null)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("EntityType is required", nameof(entityType));

        var instance = new WorkflowInstance
        {
            EntityType = entityType,
            Variant = variant,
            ParentId = parentId,
            Properties = initialProperties ?? new Dictionary<string, BsonValue>(),
            Events = new Dictionary<string, InstanceEvent>()
        };


        await repository.CreateAsync(instance);
        return instance;
    }

    /// <summary>
    /// Gets a workflow instance by ID
    /// </summary>
    public async Task<WorkflowInstance?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required", nameof(id));

        return await repository.GetByIdAsync(id);
    }

    /// <summary>
    /// Gets a workflow instance by ID or throws if not found
    /// </summary>
    public async Task<WorkflowInstance> GetByIdRequiredAsync(string id)
    {
        var instance = await GetByIdAsync(id);
        if (instance == null)
            throw new KeyNotFoundException($"WorkflowInstance with ID '{id}' not found");

        return instance;
    }

    /// <summary>
    /// Gets workflow instances by entity type
    /// </summary>
    public async Task<IEnumerable<WorkflowInstance>> GetByEntityTypeAsync(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("EntityType is required", nameof(entityType));

        return await repository.GetByEntityTypeAsync(entityType);
    }

    /// <summary>
    /// Gets workflow instances by parent ID
    /// </summary>
    public async Task<IEnumerable<WorkflowInstance>> GetByParentIdAsync(string parentId)
    {
        if (string.IsNullOrWhiteSpace(parentId))
            throw new ArgumentException("ParentId is required", nameof(parentId));

        return await repository.GetByParentIdAsync(parentId);
    }

    /// <summary>
    /// Updates a property on a workflow instance using type-safe expressions
    /// </summary>
    public async Task UpdateFieldAsync<TField>(string instanceId, Expression<Func<WorkflowInstance, TField>> field,
        TField value)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("InstanceId is required", nameof(instanceId));

        if (field == null)
            throw new ArgumentNullException(nameof(field));

        await repository.UpdateFieldAsync(instanceId, field, value);
    }

    /// <summary>
    /// Updates multiple fields on a workflow instance using MongoDB update builders
    /// </summary>
    public async Task UpdateFieldsAsync(string instanceId, UpdateDefinition<WorkflowInstance> updateDefinition)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("InstanceId is required", nameof(instanceId));

        if (updateDefinition == null)
            throw new ArgumentNullException(nameof(updateDefinition));

        await repository.UpdateFieldsAsync(instanceId, updateDefinition);
    }

    /// <summary>
    /// Updates multiple properties on a workflow instance in a single operation
    /// </summary>
    public async Task UpdatePropertiesAsync(
        string instanceId,
        Dictionary<string, BsonValue> properties)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("InstanceId is required", nameof(instanceId));

        var instance = await GetByIdRequiredAsync(instanceId);

        foreach (var (propertyPath, value) in properties)
        {
            var pathParts = propertyPath.Split('.');
            instance.SetProperty(value, pathParts);
            instance.RecordEvent($"property_updated:{propertyPath}");
        }

        await repository.UpdateAsync(instance);
    }

    /// <summary>
    /// Deletes a workflow instance
    /// </summary>
    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required", nameof(id));

        await repository.DeleteAsync(id);
    }

    /// <summary>
    /// Updates a workflow instance
    /// </summary>
    public async Task UpdateAsync(WorkflowInstance instance)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));

        await repository.UpdateAsync(instance);
    }

    /// <summary>
    /// Gets all workflow instances matching the specified expression
    /// </summary>
    public async Task<List<WorkflowInstance>> GetAllAsync(Expression<Func<WorkflowInstance, bool>> expression)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        return await repository.GetAllAsync(expression);
    }

    /// <summary>
    /// Gets a projected result from a workflow instance by ID
    /// </summary>
    public async Task<T> GetAsync<T>(string instanceId, Expression<Func<WorkflowInstance, T>> expression)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("InstanceId is required", nameof(instanceId));

        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        return await repository.GetAsync(instanceId, expression);
    }

    /// <summary>
    /// Gets a projected result from workflow instances matching the predicate
    /// </summary>
    public async Task<T> GetAsync<T>(Expression<Func<WorkflowInstance, bool>> predicate,
        Expression<Func<WorkflowInstance, T>> project)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (project == null)
            throw new ArgumentNullException(nameof(project));

        return await repository.GetAsync(predicate, project);
    }

    /// <summary>
    /// Gets workflow instances by entity type with custom projection using aggregation
    /// </summary>
    public async Task<List<Dictionary<string, BsonValue>>> GetAllByTypeAsync(string entityType,
        Dictionary<string, string> projection)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("EntityType is required", nameof(entityType));

        if (projection == null)
            throw new ArgumentNullException(nameof(projection));

        return await repository.GetAllByTypeAsync(entityType, projection);
    }

    /// <summary>
    /// Gets workflow instances by parent ID with custom projection using aggregation
    /// </summary>
    public async Task<List<Dictionary<string, BsonValue>>> GetAllByParentIdAsync(string parentId,
        Dictionary<string, string> projection)
    {
        if (string.IsNullOrWhiteSpace(parentId))
            throw new ArgumentException("ParentId is required", nameof(parentId));

        if (projection == null)
            throw new ArgumentNullException(nameof(projection));

        return await repository.GetAllByParentIdAsync(parentId, projection);
    }

    /// <summary>
    /// Gets workflow instances by multiple IDs with custom projection using aggregation
    /// </summary>
    public async Task<List<Dictionary<string, BsonValue>>> GetAllByIdAsync(string[] ids,
        Dictionary<string, string> projection)
    {
        if (ids == null || ids.Length == 0)
            throw new ArgumentException("Ids are required", nameof(ids));

        if (projection == null)
            throw new ArgumentNullException(nameof(projection));

        return await repository.GetAllByIdAsync(ids, projection);
    }
}