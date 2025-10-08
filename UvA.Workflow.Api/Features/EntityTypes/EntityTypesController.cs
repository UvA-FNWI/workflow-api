using UvA.Workflow.Api.Exceptions;
using UvA.Workflow.Api.Extensions;
using UvA.Workflow.Api.Features.EntityTypes.Dtos;

namespace UvA.Workflow.Api.Features.EntityTypes;

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
            return ErrorCode.EntityTypeNotFound;

        var dto = MapToDto(entityType);
        return Ok(dto);
    }
}