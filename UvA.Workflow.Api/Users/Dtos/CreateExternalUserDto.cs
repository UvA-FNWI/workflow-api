namespace UvA.Workflow.Api.Users.Dtos;

public record CreateExternalUserDto(
    string DisplayName,
    string Email,
    Organization? Organization = null);