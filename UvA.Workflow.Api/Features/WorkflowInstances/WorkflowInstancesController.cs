using UvA.Workflow.Api.Exceptions;
using UvA.Workflow.Api.Extensions;

namespace UvA.Workflow.Api.Features.WorkflowInstances;

public class WorkflowInstancesController(
    WorkflowInstanceService service,
    IWorkflowInstanceRepository repository) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WorkflowInstanceDto>> Create(
        [FromBody] CreateWorkflowInstanceDto dto, CancellationToken ct)
    {
        var instance = await service.Create(
            dto.EntityType,
            ct,
            dto.Variant,
            dto.ParentId,
            dto.InitialProperties?.ToDictionary(k => k.Key, v => BsonTypeMapper.MapToBsonValue(v.Value))
        );

        var result = WorkflowInstanceDto.From(instance);
        return CreatedAtAction(nameof(GetById), new { id = instance.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowInstanceDto>> GetById(string id, CancellationToken ct)
    {
        var instance = await repository.GetById(id, ct);
        if (instance == null)
            return ErrorCode.WorkflowInstancesNotFound;

        return Ok(WorkflowInstanceDto.From(instance));
    }

    [HttpGet("instances/{entityType}")]
    public async Task<ActionResult<IEnumerable<WorkflowInstanceDto>>> GetInstances(string entityType, CancellationToken ct)
    {
        var instances = await repository.GetByEntityType(entityType, ct);
        return Ok(instances.Select(WorkflowInstanceDto.From));
    }
}