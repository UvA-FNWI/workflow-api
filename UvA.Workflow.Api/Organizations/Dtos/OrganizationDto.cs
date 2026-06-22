using UvA.Workflow.Organizations;

namespace UvA.Workflow.Api.Organizations.Dtos;

/// <summary>
/// DTO for organization responses.
/// </summary>
public record OrganizationDto(string? Id, string Name)
{
    public static OrganizationDto Create(Organization organization)
        => new(organization.Id, organization.Name);
}