namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record WorkflowInstanceDto(
    string Id,
    string EntityType,
    string? CurrentStep,
    Dictionary<string, object> Properties,
    Dictionary<string, InstanceEventDto> Events,
    string? ParentId
)
{
    /// <summary>
    /// Creates a WorkflowInstanceDto from a WorkflowInstance domain entity
    /// </summary>
    public static WorkflowInstanceDto Create(WorkflowInstance instance)
    {
        return new WorkflowInstanceDto(
            instance.Id,
            instance.EntityType,
            instance.CurrentStep,
            instance.Properties.ToDictionary(k => k.Key, v => BsonTypeMapper.MapToDotNetValue(v.Value)),
            instance.Events.ToDictionary(
                kvp => kvp.Key,
                kvp => InstanceEventDto.Create(kvp.Value)
            ),
            instance.ParentId
        );
    }
}

public record InstanceEventDto(
    string Id,
    DateTime? Date
)
{
    /// <summary>
    /// Creates an InstanceEventDto from an InstanceEvent domain entity
    /// </summary>
    public static InstanceEventDto Create(InstanceEvent instanceEvent)
    {
        return new InstanceEventDto(instanceEvent.Id, instanceEvent.Date);
    }
}