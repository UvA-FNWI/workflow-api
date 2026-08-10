namespace UvA.Workflow.Api.Users.Dtos;

public record ExternalUserDto(
    string DisplayName,
    string Email,
    Organization? Organization = null,
    string? UserId = null);