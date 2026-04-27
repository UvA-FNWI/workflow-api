namespace UvA.Workflow.DataNose;

public class DataNoseUserRoleSource(IDataNoseApiClient dataNoseApiClient) : IUserRoleSource
{
    public bool CanResolve(User user) => user.AuthProvider == UserAuthProvider.Internal;

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
        => dataNoseApiClient.GetRolesByUser(user.UserName, ct);
}