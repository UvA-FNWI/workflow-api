using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Organisations.Dtos;
using UvA.Workflow.Organisations;

namespace UvA.Workflow.Api.Organisations;

public class OrganisationsController(
    IOrganisationRepository organisationRepository,
    RightsService rightsService) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrganisationDto>> Create([FromBody] CreateOrganisationDto dto,
        CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var organisation = new Organisation
        {
            Name = dto.Name
        };

        await organisationRepository.Create(organisation, ct);

        var organisationDto = OrganisationDto.Create(organisation);
        return CreatedAtAction(nameof(GetById), new { id = organisation.Id }, organisationDto);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OrganisationDto>> GetById(string id, CancellationToken ct)
    {
        var organisation = await organisationRepository.GetById(id, ct);
        if (organisation == null)
            return NotFound("OrganisationNotFound", "Organisation not found");

        return Ok(OrganisationDto.Create(organisation));
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganisationDto>>> GetAll(CancellationToken ct)
    {
        var organisations = await organisationRepository.GetAll(ct);
        return Ok(organisations.Select(OrganisationDto.Create));
    }

    [HttpGet("find")]
    public async Task<ActionResult<IEnumerable<OrganisationDto>>> Find(string query, int? limit,
        CancellationToken ct)
    {
        var resolvedLimit = Math.Clamp(limit ?? 5, 1, 100);
        var organisations = await organisationRepository.Search(query, resolvedLimit, ct);
        return Ok(organisations.Select(OrganisationDto.Create));
    }
}