namespace UvA.Workflow.Users.DataNose;

public class DataNoseUserRoleSource(IDataNoseApiClient dataNoseApiClient) : IUserRoleSource
{
    public string ProviderKey => DataNoseDirectoryKeys.ProviderKey;

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
        => dataNoseApiClient.GetRolesByUser(user.UserName, ct);
}
