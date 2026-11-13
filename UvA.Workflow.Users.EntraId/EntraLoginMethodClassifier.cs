using UvA.Workflow.Users.EntraId.Infrastructure;

namespace UvA.Workflow.Users.EntraId;

/// <summary>
/// Picks SurfConext login channel for access mails using Entra directory data instead of uid shape.
/// Access mails only know the recipient uid and email; SurfConext affiliation is not available on
/// that path yet. When the user already exists as an external invitee, classification stays external.
/// </summary>
public class EntraLoginMethodClassifier(IEntraGraphService graph, IUserRepository userRepository)
    : ILoginMethodClassifier
{
    public async Task<string?> ClassifyAsync(string? userName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return null;

        var uid = userName.Trim();
        if (uid.Contains('@', StringComparison.Ordinal))
            return null;

        if (Guid.TryParse(uid, out _))
            return "AmsterdamUmc";

        var existing = await userRepository.GetByExternalId(uid.ToLowerInvariant(), ct);
        if (existing != null && UserProviderKeys.IsExternal(existing.ProviderKey))
            return null;

        EntraLoginIdentity? identity;
        try
        {
            identity = await graph.GetLoginIdentityBySamAccountNameAsync(uid, ct);
        }
        catch (DirectoryUnavailableException)
        {
            return null;
        }

        return identity == null ? null : ClassifyIdentity(identity);
    }

    internal static string? ClassifyIdentity(EntraLoginIdentity identity)
    {
        if (!identity.AccountEnabled)
            return null;

        if (string.Equals(identity.UserType, "Guest", StringComparison.OrdinalIgnoreCase))
            return "Uva";

        if (!string.Equals(identity.UserType, "Member", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(identity.AccountState, "valid", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(identity.PrimaryAffiliation, "employee", StringComparison.OrdinalIgnoreCase))
            return "Uva";

        if (string.Equals(identity.PrimaryAffiliation, "student", StringComparison.OrdinalIgnoreCase))
            return "Uva";

        return null;
    }
}