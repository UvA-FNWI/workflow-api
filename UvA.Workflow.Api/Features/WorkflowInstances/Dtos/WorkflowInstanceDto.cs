namespace UvA.Workflow.Api.Features.WorkflowInstances.Dtos;

public record WorkflowInstanceDto(
    string Id,
    string EntityType,
    string? Variant,
    string? CurrentStep,
    Dictionary<string, object> Properties,
    Dictionary<string, InstanceEventDto> Events,
    string? ParentId
)
{
    /// <summary>
    /// Creates a WorkflowInstanceDto from a WorkflowInstance domain entity
    /// </summary>
    public static WorkflowInstanceDto From(WorkflowInstance instance)
    {
        return new WorkflowInstanceDto(
            instance.Id,
            instance.EntityType,
            instance.Variant,
            instance.CurrentStep,
            instance.Properties.ToDictionary(k => k.Key, v => BsonTypeMapper.MapToDotNetValue(v.Value)),
            instance.Events.ToDictionary(
                kvp => kvp.Key,
                kvp => InstanceEventDto.From(kvp.Value)
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
    public static InstanceEventDto From(InstanceEvent instanceEvent)
    {
        return new InstanceEventDto(instanceEvent.Id, instanceEvent.Date);
    }
}