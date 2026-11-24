using Microsoft.Extensions.Logging.Abstractions;
using UvA.Workflow.Organizations;
using UvA.Workflow.Users.EntraId.Infrastructure;

namespace UvA.Workflow.Users.EntraId;

public class EntraUserSearchSource : IUserSearchSource
{
    private readonly IEntraGraphService _graph;
    private readonly ILogger<EntraUserSearchSource> _logger;

    public EntraUserSearchSource(IEntraGraphService graph, ILogger<EntraUserSearchSource>? logger = null)
    {
        _graph = graph;
        _logger = logger ?? NullLogger<EntraUserSearchSource>.Instance;
    }

    public async Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var users = await _graph.SearchUsersAsync(query.Trim(), ct);
        var mapped = new List<UserSearchResult>();
        var skippedWithoutSam = 0;
        foreach (var user in users)
        {
            var userName = DirectoryUserName(user);
            if (userName == null)
            {
                skippedWithoutSam++;
                continue;
            }

            var email = string.IsNullOrWhiteSpace(user.Mail) ? user.UserPrincipalName : user.Mail;
            mapped.Add(new UserSearchResult(
                userName,
                user.DisplayName,
                email,
                EntraDirectoryKeys.SourceKey,
                Organization: string.IsNullOrWhiteSpace(user.Organization)
                    ? null
                    : Organization.Create(user.Organization)));
        }

        if (skippedWithoutSam > 0)
            _logger.LogInformation(
                "Skipped {Count} Entra users without onPremisesSamAccountName", skippedWithoutSam);

        var duplicateNames = mapped
            .GroupBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (duplicateNames.Count > 0)
        {
            _logger.LogWarning(
                "Omitted Entra search results for {Count} onPremisesSamAccountName values that matched more than one user",
                duplicateNames.Count);
            mapped.RemoveAll(user => duplicateNames.Contains(user.UserName));
        }

        return mapped;
    }

    /// <summary>
    /// SurfConext stores the uid in <see cref="User.UserName"/>. Only onPremisesSamAccountName
    /// is that uid. Mail nickname and the UPN local part are not substitutes.
    /// </summary>
    internal static string? DirectoryUserName(EntraUserInfo user)
        => string.IsNullOrWhiteSpace(user.SamAccountName) ? null : user.SamAccountName.Trim();
}