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
    string? Picture,
    Organization? Organization,
    bool IsExternal,
    bool IsSuperAdmin,
    bool IsEmailEditable
)
{
    public bool CanImpersonateUsers { get; init; }

    /// <summary>
    /// Creates a UserDto from a User domain entity.
    /// <paramref name="isSuperAdmin"/> indicates whether the current user can access global admin tools.
    /// It is only meaningful on the /me endpoint and defaults to false elsewhere.
    /// </summary>
    public static UserDto Create(User user, bool isSuperAdmin = false)
    {
        return new UserDto(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Email,
            user.PreferredLanguage,
            user.Picture,
            user.Organization,
            UserProviderKeys.IsExternal(user.ProviderKey),
            isSuperAdmin,
            user.InvitationState != UserInvitationState.Completed
        );
    }

    /// <summary>
    /// Creates a UserDto from an Instance User entity
    /// </summary>
    public static UserDto CreateFromInstanceUser(InstanceUser u) =>
        new(u.Id, u.UserName, u.DisplayName, u.Email, u.PreferredLanguage, u.Picture, u.Organization, u.IsExternal,
            false, u.InvitationState != UserInvitationState.Completed);
}