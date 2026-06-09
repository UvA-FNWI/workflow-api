using UvA.Workflow.Organizations;

namespace UvA.Workflow.Users.DataNose;

public class DataNoseUserDirectory(IDataNoseApiClient dataNoseApiClient) : IUserDirectory
{
    public string ProviderKey => DataNoseDirectoryKeys.ProviderKey;

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
        => dataNoseApiClient.GetRolesByUser(user.UserName, ct);

    public Task<DirectoryOrganization?> GetOrganization(string uid, CancellationToken ct = default)
        => dataNoseApiClient.GetOrganizationForUser(uid, ct);
}