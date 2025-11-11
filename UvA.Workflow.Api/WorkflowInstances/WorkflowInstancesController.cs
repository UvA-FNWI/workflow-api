using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances;

public class WorkflowInstancesController(
    WorkflowInstanceService service,
    RightsService rightsService,
    ContextService contextService,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory,
    IWorkflowInstanceRepository repository) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WorkflowInstanceDto>> Create(
        [FromBody] CreateWorkflowInstanceDto input, CancellationToken ct)
    {
        var actions = input.ParentId == null
            ? await rightsService.GetAllowedActions(input.EntityType, RoleAction.CreateInstance)
            : [];
        if (actions.Length == 0)
            return Forbid();

        var instance = await service.Create(
            input.EntityType,
            ct,
            actions.First().UserProperty,
            input.ParentId,
            input.InitialProperties?.ToDictionary(k => k.Key, v => BsonTypeMapper.MapToBsonValue(v.Value))
        );

        await contextService.UpdateCurrentStep(instance, ct);

        var result = await workflowInstanceDtoFactory.Create(instance, ct);

        return CreatedAtAction(nameof(GetById), new { id = instance.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowInstanceDto>> GetById(string id, CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        var result = await workflowInstanceDtoFactory.Create(instance, ct);

        return Ok(result);
    }

    [HttpGet("instances/{entityType}")]
    public async Task<ActionResult<IEnumerable<WorkflowInstanceBasicDto>>> GetInstances(string entityType,
        CancellationToken ct)
    {
        var instances = await repository.GetByEntityType(entityType, ct);
        return Ok(instances.Select(i => new WorkflowInstanceBasicDto(i.Id, i.CurrentStep)));
    }
}