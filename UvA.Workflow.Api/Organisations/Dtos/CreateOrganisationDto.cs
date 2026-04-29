using System.ComponentModel.DataAnnotations;

namespace UvA.Workflow.Api.Organisations.Dtos;

/// <summary>
/// DTO for creating a new organisation.
/// </summary>
public record CreateOrganisationDto([Required] string Name);