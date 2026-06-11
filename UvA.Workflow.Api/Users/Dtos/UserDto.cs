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
    bool IsSuperAdmin,
    bool IsPending
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
            isSuperAdmin,
            user.InvitationState == UserInvitationState.Pending
        );
    }

    /// <summary>
    /// Creates a UserDto from an Instance User entity
    /// </summary>
    public static UserDto CreateFromInstanceUser(InstanceUser u) =>
        new(u.Id, u.UserName, u.DisplayName, u.Email, u.PreferredLanguage, u.Organization, u.IsExternal,
            false, u.InvitationState == UserInvitationState.Pending);
}