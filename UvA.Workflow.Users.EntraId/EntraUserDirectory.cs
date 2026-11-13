using Microsoft.Extensions.Caching.Memory;
using UvA.Workflow.Organizations;
using UvA.Workflow.Users.EntraId.Infrastructure;

namespace UvA.Workflow.Users.EntraId;

/// <summary>
/// Directory registered only when <c>UserProvider</c> is <c>entra</c> or <c>entra-id</c>.
/// Workflow roles are not read from Entra app roles or groups. A configured bootstrap uid
/// receives SystemAdmin. The organisation comes from extension attributes, looked up by
/// SAM account name, and is reused for a day so login does not call Graph every time.
/// </summary>
public class EntraUserDirectory : IUserDirectory
{
    public static readonly TimeSpan OrganizationCacheDuration = TimeSpan.FromHours(24);
    public static readonly TimeSpan LoginLookupTimeout = TimeSpan.FromSeconds(5);

    private readonly IEntraGraphService _graph;
    private readonly SystemAdminBootstrapOptions _systemAdmins;
    private readonly IMemoryCache? _cache;
    private readonly ILogger<EntraUserDirectory> _logger;

    public EntraUserDirectory(
        IEntraGraphService graph,
        IOptions<SystemAdminBootstrapOptions>? systemAdmins = null,
        IMemoryCache? cache = null,
        ILogger<EntraUserDirectory>? logger = null)
    {
        _graph = graph;
        _systemAdmins = systemAdmins?.Value ?? new SystemAdminBootstrapOptions();
        _cache = cache;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EntraUserDirectory>.Instance;
    }

    public string ProviderKey => EntraDirectoryKeys.ProviderKey;

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
    {
        IEnumerable<string> roles = _systemAdmins.IsListed(user.UserName)
            ? [SystemAdminBootstrapOptions.RoleName]
            : [];
        return Task.FromResult(roles);
    }

    public async Task<Organization?> GetOrganization(string uid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uid))
            return null;

        var cacheKey = "entra:org:" + uid.Trim().ToLowerInvariant();
        if (_cache != null && _cache.TryGetValue(cacheKey, out string? cached))
            return string.IsNullOrEmpty(cached) ? null : Organization.Create(cached);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(LoginLookupTimeout);

        EntraUserInfo? entraUser;
        try
        {
            entraUser = await _graph.GetUserBySamAccountNameAsync(uid, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Entra organization lookup timed out");
            return null;
        }
        catch (DirectoryUnavailableException)
        {
            _logger.LogWarning("Entra organization lookup skipped because the directory is unavailable");
            return null;
        }

        var name = string.IsNullOrWhiteSpace(entraUser?.Organization) ? "" : entraUser.Organization;
        _cache?.Set(cacheKey, name, OrganizationCacheDuration);
        return name.Length == 0 ? null : Organization.Create(name);
    }
}