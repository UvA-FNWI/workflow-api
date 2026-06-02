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
    bool IsPending
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
            user.PreferredLanguage,
            user.Organization,
            UserProviderKeys.IsExternal(user.ProviderKey),
            user.InvitationState == UserInvitationState.Pending
        );
    }

    /// <summary>
    /// Creates a UserDto from an Instance User entity
    /// </summary>
    public static UserDto CreateFromInstanceUser(InstanceUser u) =>
        new(u.Id, u.UserName, u.DisplayName, u.Email, u.PreferredLanguage, u.Organization, u.IsExternal,
            u.InvitationState == UserInvitationState.Pending);
}