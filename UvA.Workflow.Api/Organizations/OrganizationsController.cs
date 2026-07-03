using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Organizations.Dtos;

namespace UvA.Workflow.Api.Organizations;

public class OrganizationsController(
    IOrganizationService organizationService) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> Create([FromBody] CreateOrganizationDto dto,
        CancellationToken ct)
    {
        var organization = await organizationService.GetOrCreateOrganization(dto.Name, ct);

        var organizationDto = OrganizationDto.Create(organization);
        return CreatedAtAction(nameof(GetById), new { id = organization.Id }, organizationDto);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OrganizationDto>> GetById(string id, CancellationToken ct)
    {
        var organization = await organizationService.GetOrganization(id, ct);
        if (organization == null)
            return NotFound("OrganizationNotFound", "Organization not found");

        return Ok(OrganizationDto.Create(organization));
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationDto>>> GetAll(CancellationToken ct)
    {
        var organizations = await organizationService.GetAll(ct);
        return Ok(organizations.Select(OrganizationDto.Create));
    }

    [HttpGet("find")]
    public async Task<ActionResult<IEnumerable<OrganizationDto>>> Find(string query, int? limit,
        CancellationToken ct)
    {
        var resolvedLimit = Math.Clamp(limit ?? 5, 1, 100);
        var organizations = await organizationService.Search(query, resolvedLimit, ct);
        return Ok(organizations.Select(OrganizationDto.Create));
    }
}