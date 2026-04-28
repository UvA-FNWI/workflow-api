namespace UvA.Workflow.Api.Users.Dtos;

/// <summary>
/// DTO for user response
/// </summary>
public record UserDto(
    string Id,
    string UserName,
    string DisplayName,
    string Email,
    Organization? Organization,
    bool IsExternal
)
{
    /// <summary>
    /// Creates a UserDto from a User domain entity
    /// </summary>
    public static UserDto Create(User user)
    {
        return new UserDto(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.Organization,
            user.AuthProvider == UserAuthProvider.EduId
        );
    }
}