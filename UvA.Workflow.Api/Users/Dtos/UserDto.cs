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
    bool IsSuperAdmin
)
{
    /// <summary>
    /// Creates a UserDto from a User domain entity.
    /// <paramref name="isSuperAdmin"/> reflects whether the *current* request's user is a DataNose
    /// super admin, so it is only meaningful on the /me endpoint; it defaults to false elsewhere.
    /// </summary>
    public static UserDto Create(User user, bool isSuperAdmin = false)
    {
        return new UserDto(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.PreferredLanguage,
            user.Organization,
            UserProviderKeys.IsExternal(user.ProviderKey),
            isSuperAdmin
        );
    }
}