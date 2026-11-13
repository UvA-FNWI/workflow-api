using Microsoft.Extensions.Configuration;

namespace UvA.Workflow.Users.EntraId;

public class EntraGraphOptions
{
    public const string Section = "EntraId";
    public const int AttemptTimeoutSeconds = 10;

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Authority of the first tenant, including the tenant id. Extra tenants are listed in
    /// <see cref="Tenants"/>. The same client id and secret are used for every tenant.
    /// </summary>
    public string? Authority { get; set; }

    public List<EntraTenantOptions> Tenants { get; set; } = [];

    /// <summary>
    /// Total time budget for one Graph call, including retries. Each attempt is capped
    /// separately at <see cref="AttemptTimeoutSeconds"/>.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException($"{Section}:ClientId is required for Entra ID Graph");

        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException($"{Section}:ClientSecret is required for Entra ID Graph");

        if (ResolvedTenants().Count == 0)
            throw new InvalidOperationException(
                $"{Section}:Authority or {Section}:Tenants is required for Entra ID Graph");

        if (RequestTimeoutSeconds <= AttemptTimeoutSeconds || RequestTimeoutSeconds > 120)
            throw new InvalidOperationException(
                $"{Section}:RequestTimeoutSeconds must be greater than {AttemptTimeoutSeconds} and at most 120");
    }

    /// <summary>
    /// The authority tenant first, then <see cref="Tenants"/>, without duplicate tenant ids.
    /// An authority of <c>common</c> or <c>organizations</c> is not a directory and is ignored.
    /// </summary>
    public IReadOnlyList<EntraTenantOptions> ResolvedTenants()
    {
        var ids = new List<string>();
        var fromAuthority = TenantIdFromAuthority(Authority);
        if (fromAuthority != null)
            ids.Add(fromAuthority);

        foreach (var tenant in Tenants)
        {
            if (!string.IsNullOrWhiteSpace(tenant.TenantId))
                ids.Add(tenant.TenantId.Trim());
        }

        return ids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new EntraTenantOptions { TenantId = id })
            .ToArray();
    }

    /// <summary>
    /// Reads <c>Tenants</c> as either <c>[{ "TenantId": "..." }]</c> or <c>["..."]</c>.
    /// Configuration binding of a string array does not fill <see cref="EntraTenantOptions.TenantId"/>.
    /// </summary>
    internal void BindTenants(IConfiguration section)
    {
        var bound = new List<EntraTenantOptions>();
        foreach (var child in section.GetSection(nameof(Tenants)).GetChildren())
        {
            var id = child.Value ?? child[nameof(EntraTenantOptions.TenantId)];
            if (!string.IsNullOrWhiteSpace(id))
                bound.Add(new EntraTenantOptions { TenantId = id.Trim() });
        }

        Tenants = bound;
    }

    internal static string? TenantIdFromAuthority(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority) ||
            !Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri))
            return null;

        var segment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(segment))
            return null;
        if (segment.Equals("common", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("organizations", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("consumers", StringComparison.OrdinalIgnoreCase))
            return null;

        return segment;
    }
}

public sealed class EntraTenantOptions
{
    public string? TenantId { get; set; }

    public string Authority => $"https://login.microsoftonline.com/{TenantId}/";
}

public sealed class EntraGraphOptionsValidator : IValidateOptions<EntraGraphOptions>
{
    public ValidateOptionsResult Validate(string? name, EntraGraphOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}