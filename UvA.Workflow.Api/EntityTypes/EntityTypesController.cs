using UvA.Workflow.Api.EntityTypes.Dtos;
using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.EntityTypes;

public class EntityTypesController(ModelService modelService) : ApiControllerBase
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
            return NotFound("EntityTypeNotFound", $"Entity type '{name}' not found.");

        var dto = MapToDto(entityType);
        return Ok(dto);
    }
}