using UvA.Workflow.Organizations;

namespace UvA.Workflow.Users.EntraId;

/// <summary>
/// Directory registered only when <c>UserProvider</c> is <c>entra</c> or <c>entra-id</c>.
/// Workflow roles are not read from Entra app roles or groups. Organisation lookup is not
/// sourced from Graph in this slice.
/// </summary>
public class EntraUserDirectory : IUserDirectory
{
    public string ProviderKey => EntraDirectoryKeys.ProviderKey;

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
        => Task.FromResult<IEnumerable<string>>([]);

    public Task<Organization?> GetOrganization(string uid, CancellationToken ct = default)
        => Task.FromResult<Organization?>(null);
}