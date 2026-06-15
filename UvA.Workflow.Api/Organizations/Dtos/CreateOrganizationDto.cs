using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Api.Organizations.Dtos;

/// <summary>
/// DTO for creating a new organization.
/// </summary>
public record CreateOrganizationDto([Required] string Name);