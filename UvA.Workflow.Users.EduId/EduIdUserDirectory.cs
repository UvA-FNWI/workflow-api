namespace UvA.Workflow.Users.EduId;

public class EduIdUserDirectory : IUserDirectory
{
    public string ProviderKey => EduIdDirectoryKeys.ProviderKey;

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<string>());

    public Task<Organization?> GetOrganization(string uid, CancellationToken ct = default)
        => Task.FromResult<Organization?>(null);
}