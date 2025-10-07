using UvA.Workflow.Api.Exceptions;

namespace UvA.Workflow.Api.Features.WorkflowInstances;

    [ApiController]
    [Route("api/workflow-instances")]
    public class WorkflowInstancesController(WorkflowInstanceService service) : ControllerBase
    {

    [HttpPost]
    public async Task<ActionResult<WorkflowInstanceDto>> Create(
        [FromBody] CreateWorkflowInstanceDto dto)
    {
        var instance = await service.CreateAsync(
            dto.EntityType,
            dto.Variant,
            dto.ParentId,
            dto.InitialProperties?.ToDictionary(k => k.Key, v => BsonTypeMapper.MapToBsonValue(v.Value))
        );

        var result = WorkflowInstanceDto.From(instance);
        return CreatedAtAction(nameof(GetById), new { id = instance.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowInstanceDto>> GetById(string id)
    {
        var instance = await service.GetByIdAsync(id);
        if (instance == null)
            return ErrorCode.WorkflowInstancesNotFound;

        return Ok(WorkflowInstanceDto.From(instance));
    }

    [HttpGet("instances/{entityType}")]
    public async Task<ActionResult<IEnumerable<WorkflowInstanceDto>>> GetInstances(string entityType)
    {
        var instances = await service.GetByEntityTypeAsync(entityType);
        return Ok(instances.Select(WorkflowInstanceDto.From));
    }
}