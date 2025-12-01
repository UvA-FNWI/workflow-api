using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;

namespace UvA.Workflow.Api.WorkflowDefinitions.Dto;

public class WorkflowDefinitionsController(ModelService modelService) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<WorkflowDefinitionDto>> GetAll()
    {
        var workflowDefinitionDtos = modelService.WorkflowDefinitions.Values
            .Where(t => t.Screens.Any())
            .Select(WorkflowDefinitionDto.Create)
            .OrderBy(et => et.Index ?? int.MaxValue)
            .ThenBy(et => et.Name);

        return Ok(workflowDefinitionDtos);
    }

    [HttpGet("{name}")]
    public ActionResult<WorkflowDefinitionDto> GetByName(string name)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(name, out var workflowDefinition))
            return NotFound("EntityTypeNotFound", $"Entity type '{name}' not found.");

        var dto = WorkflowDefinitionDto.Create(workflowDefinition);
        return Ok(dto);
    }
}