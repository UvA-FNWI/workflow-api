namespace UvA.Workflow.Api.Users.Dtos;

/// <summary>
/// DTO for user response
/// </summary>
public record UserDto(
    string Id,
    string ExternalId,
    string DisplayName,
    string Email
)
{
    /// <summary>
    /// Creates a UserDto from a User domain entity
    /// </summary>
    public static UserDto Create(User user)
    {
        return new UserDto(
            user.Id,
            user.ExternalId,
            user.DisplayName,
            user.Email
        );
    }
}