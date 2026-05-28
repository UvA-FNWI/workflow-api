using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions;

public class WorkflowDefinitionsController(ModelService modelService, RightsService rightsService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkflowDefinitionDto>>> GetAll(CancellationToken ct)
    {
        var workflowDefinitionDtos = await modelService.WorkflowDefinitions.Values
            .Where(t => t.Screens.Any())
            .OrderBy(et => et.Index ?? int.MaxValue)
            .ThenBy(et => et.Name)
            .SelectAsync(async (et, _) => WorkflowDefinitionDto.Create(et, await GetAllowedActions(et.Name)), ct);

        return Ok(workflowDefinitionDtos);
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetByName(string name)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(name, out var workflowDefinition))
            return NotFound("EntityTypeNotFound", $"Entity type '{name}' not found.");

        var allowedActions = await GetAllowedActions(workflowDefinition.Name);
        var dto = WorkflowDefinitionDto.Create(workflowDefinition, allowedActions);
        return Ok(dto);
    }

    private async Task<RoleAction[]> GetAllowedActions(string workflowDefinition)
        => (await rightsService.GetAllowedActions(workflowDefinition, Enum.GetValues<RoleAction>()))
            .Select(a => a.Type)
            .Distinct()
            .ToArray();
}