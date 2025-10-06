using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using UvA.Workflow.Api.Features.WorkflowInstances.Dtos;
using Uva.Workflow.WorkflowInstances;

namespace UvA.Workflow.Api.Features.WorkflowInstances;

    [ApiController]
    [Route("api/workflow-instances")]
    public class WorkflowInstancesController(WorkflowInstanceService service) : ControllerBase
    {

    [HttpPost]
    public async Task<ActionResult<WorkflowInstanceDto>> Create(
        [FromBody] CreateWorkflowInstanceDto dto)
    {
        try
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
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowInstanceDto>> GetById(string id)
    {
        try
        {
            var instance = await service.GetByIdAsync(id);

            if (instance == null)
                return NotFound(new { error = $"WorkflowInstance with ID '{id}' not found" });

            return Ok(WorkflowInstanceDto.From(instance));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("instances/{entityType}")]
    public async Task<ActionResult<IEnumerable<WorkflowInstanceDto>>> GetInstances(string entityType)
    {
        try
        {
            var instances = await service.GetByEntityTypeAsync(entityType);
            return Ok(instances.Select(WorkflowInstanceDto.From));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}