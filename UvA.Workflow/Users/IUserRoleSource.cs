namespace UvA.Workflow.Users;

public interface IUserRoleSource
{
    bool CanResolve(User user);

    Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default);
}