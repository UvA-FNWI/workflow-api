namespace UvA.Workflow.Api.Features.Users.Dtos;

/// <summary>
/// DTO for creating a new user
/// </summary>
public record CreateUserDto(
    string ExternalId,
    string DisplayName,
    string Email
);
