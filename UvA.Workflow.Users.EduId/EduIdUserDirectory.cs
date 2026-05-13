namespace UvA.Workflow.Users.EduId;

public class EduIdUserDirectory : IUserRoleSource
{
    public string ProviderKey => EduIdDirectoryKeys.ProviderKey;

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<string>());
}