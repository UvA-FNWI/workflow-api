namespace UvA.Workflow.Users.EntraId.Infrastructure;

/// <summary>
/// App-only Microsoft Graph client for directory lookup. Does not read app roles or groups.
/// Mail uses <c>UvA.Workflow.Notifications.Graph</c> with its own app registration.
/// </summary>
public interface IEntraGraphService
{
    Task<IReadOnlyList<EntraUserInfo>> SearchUsersAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Exact lookup on <c>onPremisesSamAccountName</c>. Returns null when Graph returns
    /// nobody, more than one user, or a user without that property.
    /// </summary>
    Task<EntraUserInfo?> GetUserBySamAccountNameAsync(string samAccountName, CancellationToken ct = default);

    /// <summary>
    /// Broad SAM lookup for access-mail login classification (any user type, no employee-only filter).
    /// Returns null when Graph is unavailable, nobody matches, or the match is ambiguous.
    /// </summary>
    Task<EntraLoginIdentity?> GetLoginIdentityBySamAccountNameAsync(string samAccountName,
        CancellationToken ct = default);
}

/// <summary>Directory fields used to choose SurfConext vs eduID in access mails.</summary>
public record EntraLoginIdentity(
    bool AccountEnabled,
    string UserType,
    string? AccountState,
    string? PrimaryAffiliation);

public record EntraUserInfo(
    string UserPrincipalName,
    string DisplayName,
    string Mail,
    string? SamAccountName = null,
    string? Organization = null);