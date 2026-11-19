using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions;

public class WorkflowDefinitionsController(
    ModelService modelService,
    RightsService rightsService,
    InstanceAuthorizationFilterService authorizationFilterService)
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

    /// <summary>
    /// Returns workflow definitions with coordinator screens accessible through global or inherited roles.
    /// Used by the frontend landing page; direct instance roles use the personal overview instead.
    /// </summary>
    [HttpGet("Accessible")]
    public async Task<ActionResult<IEnumerable<WorkflowDefinitionDto>>> GetAccessible(CancellationToken ct)
    {
        var definitions = modelService.WorkflowDefinitions.Values
            .Where(t => t.Screens.Any())
            .OrderBy(et => et.Index ?? int.MaxValue)
            .ThenBy(et => et.Name)
            .ToArray();

        var dtos = new List<WorkflowDefinitionDto>(definitions.Length);
        foreach (var definition in definitions)
        {
            if (!await authorizationFilterService.CanAccessWorkflowOverview(definition.Name, ct))
                continue;

            var groupedParent = definition.Parent;

            while (groupedParent is not null &&
                   (!groupedParent.GroupChildScreens || groupedParent.Screens.Count == 0))
            {
                groupedParent = groupedParent.Parent;
            }

            if (groupedParent is not null)
            {
                // Add titles of all direct children of the grouped parent
                var children = modelService.WorkflowDefinitions.Values
                    .Where(d => d.Parent == groupedParent)
                    .OrderBy(d => d.Index ?? int.MaxValue)
                    .ThenBy(d => d.Name);
                var childNames = new List<BilingualString>();

                foreach (var child in children)
                {
                    childNames.Add(child.Title ?? child.Name);
                }

                groupedParent.Description = childNames.Aggregate((a, b) => a + (BilingualString)", " + b);
            }

            dtos.Add(WorkflowDefinitionDto.Create(groupedParent ?? definition));
        }

        return Ok(dtos
            .GroupBy(dto => dto.Name)
            .Select(group => group.FirstOrDefault(dto => dto.Description is not null) ?? group.First()));
    }
}