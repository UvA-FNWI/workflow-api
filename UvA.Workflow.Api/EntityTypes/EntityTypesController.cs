using UvA.Workflow.Api.EntityTypes.Dtos;
using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.EntityTypes;

public class EntityTypesController(ModelService modelService) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<EntityTypeDto>> GetAll()
    {
        var entityTypes = modelService.EntityTypes.Values
            .Select(EntityTypeDto.Create)
            .OrderBy(et => et.Index ?? int.MaxValue)
            .ThenBy(et => et.Name);

        return Ok(entityTypes);
    }

    [HttpGet("{name}")]
    public ActionResult<EntityTypeDto> GetByName(string name)
    {
        if (!modelService.EntityTypes.TryGetValue(name, out var entityType))
            return NotFound("EntityTypeNotFound", $"Entity type '{name}' not found.");

        var dto = EntityTypeDto.Create(entityType);
        return Ok(dto);
    }
}