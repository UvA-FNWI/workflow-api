using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions;

public class WorkflowDefinitionsController(ModelService modelService, RightsService rightsService)
    : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkflowDefinitionDto>>> GetAll(
        [FromQuery] bool includeAll = false)
    {
        var definitions = modelService.WorkflowDefinitions.Values
            .Where(t => includeAll || t.Screens.Any())
            .OrderBy(et => et.Index ?? int.MaxValue)
            .ThenBy(et => et.Name)
            .ToArray();

        var dtos = new List<WorkflowDefinitionDto>(definitions.Length);
        foreach (var definition in definitions)
        {
            var canCreateInstance = await rightsService.CanAny(definition.Name, RoleAction.CreateInstance);
            dtos.Add(WorkflowDefinitionDto.Create(definition, canCreateInstance));
        }

        return Ok(dtos);
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetByName(string name)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(name, out var workflowDefinition))
            return NotFound("EntityTypeNotFound", $"Entity type '{name}' not found.");

        var canCreateInstance = await rightsService.CanAny(name, RoleAction.CreateInstance);
        var dto = WorkflowDefinitionDto.Create(workflowDefinition, canCreateInstance);
        return Ok(dto);
    }
}