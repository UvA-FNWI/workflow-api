using Microsoft.Identity.Client;

namespace UvA.Workflow.Users.EntraId.Infrastructure;

internal interface IEntraAccessTokenSource
{
    /// <summary>Tenant ids in configuration order. The authority tenant is first.</summary>
    IReadOnlyList<string> TenantIds { get; }

    Task<string> GetAccessTokenAsync(string tenantId, CancellationToken ct);
}

/// <summary>
/// Asks MSAL for an app-only Graph token per tenant. One confidential client is built per
/// tenant from the same client id and secret. MSAL caches each token; this type does not.
/// </summary>
internal sealed class MsalEntraAccessTokenSource : IEntraAccessTokenSource
{
    private static readonly string[] AppOnlyScopes = ["https://graph.microsoft.com/.default"];

    private readonly Dictionary<string, IConfidentialClientApplication> _applications;

    public MsalEntraAccessTokenSource(EntraGraphOptions options)
    {
        var tenants = options.ResolvedTenants();
        TenantIds = tenants.Select(tenant => tenant.TenantId!).ToArray();
        _applications = tenants.ToDictionary(
            tenant => tenant.TenantId!,
            tenant => ConfidentialClientApplicationBuilder
                .Create(options.ClientId)
                .WithClientSecret(options.ClientSecret)
                .WithAuthority(tenant.Authority)
                .Build(),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> TenantIds { get; }

    public async Task<string> GetAccessTokenAsync(string tenantId, CancellationToken ct)
    {
        if (!_applications.TryGetValue(tenantId, out var application))
            throw new InvalidOperationException($"No Entra token client is configured for tenant {tenantId}");

        var result = await application.AcquireTokenForClient(AppOnlyScopes).ExecuteAsync(ct);
        return result.AccessToken;
    }
}