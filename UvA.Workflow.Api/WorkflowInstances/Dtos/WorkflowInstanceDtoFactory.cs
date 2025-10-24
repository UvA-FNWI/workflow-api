using UvA.Workflow.Api.EntityTypes.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class WorkflowInstanceDtoFactory(InstanceService instanceService, ModelService modelService)
{
    /// <summary>
    /// Creates a WorkflowInstanceDto from a WorkflowInstance domain entity
    /// </summary>
    public async Task<WorkflowInstanceDto> Create(WorkflowInstance instance, CancellationToken ct)
    {
        var actions = await instanceService.GetAllowedActions(instance, ct);
        
        return new WorkflowInstanceDto(
            instance.Id,
            EntityTypeDto.Create(modelService.EntityTypes[instance.EntityType]),
            instance.CurrentStep,
            instance.Properties.ToDictionary(k => k.Key, v => BsonTypeMapper.MapToDotNetValue(v.Value)),
            instance.Events.ToDictionary(
                kvp => kvp.Key,
                kvp => InstanceEventDto.Create(kvp.Value)
            ),
            instance.ParentId,
            actions.Select(ActionDto.Create).ToArray(),
            [],
            [],
            [],
            []
        );
    }
}