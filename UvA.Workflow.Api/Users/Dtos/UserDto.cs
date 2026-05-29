namespace UvA.Workflow.Api.Users.Dtos;

/// <summary>
/// DTO for user response
/// </summary>
public record UserDto(
    string Id,
    string UserName,
    string DisplayName,
    string Email,
    string? PreferredLanguage,
    Organization? Organization,
    bool IsExternal,
    bool IsAdmin
)
{
    /// <summary>
    /// Creates a UserDto from a User domain entity.
    /// <paramref name="isAdmin"/> reflects whether the *current* request's user has global admin
    /// rights, so it is only meaningful on the /me endpoint; it defaults to false elsewhere.
    /// </summary>
    public static UserDto Create(User user, bool isAdmin = false)
    {
        return new UserDto(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.PreferredLanguage,
            user.Organization,
            UserProviderKeys.IsExternal(user.ProviderKey),
            isAdmin
        );
    }
}