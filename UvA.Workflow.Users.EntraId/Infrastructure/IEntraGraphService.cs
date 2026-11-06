namespace UvA.Workflow.Users.EntraId.Infrastructure;

/// <summary>
/// App-only Microsoft Graph client for directory lookup. Does not read app roles or groups.
/// Mail uses <c>UvA.Workflow.Notifications.Graph</c> with its own app registration.
/// </summary>
public interface IEntraGraphService
{
    Task<IReadOnlyList<EntraUserInfo>> SearchUsersAsync(string query, CancellationToken ct = default);

    Task<EntraUserInfo?> GetUserByUpnAsync(string upn, CancellationToken ct = default);
}

public record EntraUserInfo(
    string UserPrincipalName,
    string DisplayName,
    string Mail,
    string? SamAccountName = null,
    string? MailNickname = null);