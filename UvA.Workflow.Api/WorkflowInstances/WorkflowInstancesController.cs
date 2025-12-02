using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances;

public class WorkflowInstancesController(
    IUserService userService,
    WorkflowInstanceService service,
    RightsService rightsService,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory,
    IWorkflowInstanceRepository repository,
    InstanceService instanceService
) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WorkflowInstanceDto>> Create(
        [FromBody] CreateWorkflowInstanceDto input, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null) return Unauthorized();
        var actions = input.ParentId == null
            ? await rightsService.GetAllowedActions(input.WorkflowDefinition, RoleAction.CreateInstance)
            : [];
        if (actions.Length == 0)
            return Forbid();

        var instance = await service.Create(
            input.WorkflowDefinition,
            user,
            ct,
            actions.First().UserProperty,
            input.ParentId,
            input.InitialProperties?.ToDictionary(k => k.Key, v => BsonTypeMapper.MapToBsonValue(v.Value))
        );

        await instanceService.UpdateCurrentStep(instance, ct);

        var result = await workflowInstanceDtoFactory.Create(instance, ct);

        return CreatedAtAction(nameof(GetById), new { id = instance.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowInstanceDto>> GetById(string id, CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        if (!await rightsService.Can(instance, RoleAction.View))
            return Forbidden();

        await instanceService.UpdateCurrentStep(instance, ct);

        var result = await workflowInstanceDtoFactory.Create(instance, ct);

        return Ok(result);
    }

    [HttpGet("instances/{workflowDefinition}")]
    public async Task<ActionResult<IEnumerable<WorkflowInstanceBasicDto>>> GetInstances(string workflowDefinition,
        CancellationToken ct)
    {
        var instances = await repository.GetByWorkflowDefinition(workflowDefinition, ct);
        return Ok(instances.Select(i => new WorkflowInstanceBasicDto(i.Id, i.CurrentStep)));
    }
}