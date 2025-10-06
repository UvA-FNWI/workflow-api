using UvA.Workflow.Api.Features.EntityTypes.Dtos;

namespace UvA.Workflow.Api.Features.EntityTypes;

[ApiController]
[Route("api/entity-types")]
public class EntityTypesController(ModelService modelService) : ControllerBase
{
    private static EntityTypeDto MapToDto(EntityType entityType)
    {
        return new EntityTypeDto(
            entityType.Name,
            entityType.Title,
            entityType.TitlePlural,
            entityType.Index,
            entityType.IsAlwaysVisible,
            entityType.InheritsFrom,
            entityType.IsEmbedded
        );
    }

    [HttpGet]
    public ActionResult<IEnumerable<EntityTypeDto>> GetAll()
    {
        var entityTypes = modelService.EntityTypes.Values
            .Select(MapToDto)
            .OrderBy(et => et.Index ?? int.MaxValue)
            .ThenBy(et => et.Name);

        return Ok(entityTypes);
    }

    [HttpGet("{name}")]
    public ActionResult<EntityTypeDto> GetByName(string name)
    {
        if (!modelService.EntityTypes.TryGetValue(name, out var entityType))
        {
            return NotFound(new { error = $"EntityType with name '{name}' not found" });
        }

        var dto = MapToDto(entityType);
        return Ok(dto);
    }
}