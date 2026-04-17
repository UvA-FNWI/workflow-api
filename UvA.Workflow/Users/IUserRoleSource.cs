namespace UvA.Workflow.Users;

public interface IUserRoleSource
{
    string ProviderKey { get; }

    Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default);
}