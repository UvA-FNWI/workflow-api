namespace UvA.Workflow.Users;

/// <summary>
/// A per-provider external directory (e.g. DataNose, EduId) that exposes the user attributes the
/// application sources externally: roles and organisation.
/// </summary>
public interface IUserDirectory
{
    string ProviderKey { get; }

    Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default);

    /// <summary>
    /// Looks up a user's organisation by their uid. Returns null when the directory has no
    /// organisation for them.
    /// </summary>
    Task<DirectoryOrganization?> GetOrganization(string uid, CancellationToken ct = default);
}