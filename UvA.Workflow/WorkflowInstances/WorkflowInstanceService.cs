using UvA.Workflow.Events;

namespace UvA.Workflow.WorkflowInstances;

public class WorkflowInstanceService(
    ModelService modelService,
    IWorkflowInstanceRepository repository)
{
    /// <summary>
    /// Creates a new workflow instance
    /// </summary>
    public async Task<WorkflowInstance> Create(
        string workflowDefinition,
        User createdBy,
        CancellationToken ct,
        string? userProperty = null,
        string? parentId = null,
        Dictionary<string, BsonValue>? initialProperties = null)
    {
        if (string.IsNullOrWhiteSpace(workflowDefinition))
            throw new ArgumentException("WorkflowDefinition is required", nameof(workflowDefinition));

        var instance = new WorkflowInstance
        {
            WorkflowDefinition = workflowDefinition,
            ParentId = parentId,
            CreatedOn = DateTime.Now,
            Properties = initialProperties ?? new Dictionary<string, BsonValue>(),
            Events = new Dictionary<string, InstanceEvent>()
        };

        if (userProperty != null)
        {
            var user = createdBy.ToBsonDocument();
            var property = modelService.WorkflowDefinitions[workflowDefinition].Properties.Get(userProperty);
            instance.Properties[userProperty] = property.IsArray ? new BsonArray { user } : user;
        }

        await repository.Create(instance, ct);
        return instance;
    }

    /// <summary>
    /// Updates multiple properties on a workflow instance in a single operation
    /// </summary>
    public async Task UpdateProperties(
        string instanceId,
        Dictionary<string, BsonValue> properties,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("InstanceId is required", nameof(instanceId));

        var instance = await repository.GetById(instanceId, ct);
        if (instance == null)
            throw new ArgumentException("Instance not found", nameof(instanceId));

        foreach (var (propertyPath, value) in properties)
        {
            var pathParts = propertyPath.Split('.');
            instance.SetProperty(value, pathParts);
            instance.RecordEvent($"property_updated:{propertyPath}");
        }

        await repository.Update(instance, ct);
    }
}