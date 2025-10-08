namespace UvA.Workflow.WorkflowInstances;

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
    /// Updates multiple properties on a workflow instance in a single operation
    /// </summary>
    public async Task UpdatePropertiesAsync(
        string instanceId,
        Dictionary<string, BsonValue> properties)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("InstanceId is required", nameof(instanceId));

        var instance = await repository.GetByIdAsync(instanceId);
        if (instance == null)
            throw new ArgumentException("Instance not found", nameof(instanceId));
 
        foreach (var (propertyPath, value) in properties)
        {
            var pathParts = propertyPath.Split('.');
            instance.SetProperty(value, pathParts);
            instance.RecordEvent($"property_updated:{propertyPath}");
        }

        await repository.UpdateAsync(instance);
    }
}