using UvA.Workflow.Organisations;

namespace UvA.Workflow.Api.Organisations.Dtos;

/// <summary>
/// DTO for organisation responses.
/// </summary>
public record OrganisationDto(string Id, string Name)
{
    public static OrganisationDto Create(Organisation organisation)
        => new(organisation.Id, organisation.Name);
}