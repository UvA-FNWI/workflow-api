using System.Security.Claims;
using UvA.Workflow.DataNose;

namespace UvA.Workflow.Users;

public class UserService(IDataNoseApiClient dataNoseApiClient) : IUserService
{
    public async Task<GlobalRole[]> GetRoles(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var uid = principal.Claims.First(c => c.Type == "urn:uvanetid").Value;
        var roles = await dataNoseApiClient.GetRolesByUser(uid, ct);
        return roles.Select(r => new GlobalRole(r)).ToArray();
    }

    public Task<ExternalUser> GetUserInfo(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var id = principal.Claims.First(c => c.Type == "urn:uvanetid").Value;
        var displayName = principal.Claims.First(c => c.Type == "name").Value;
        var email = principal.Claims.First(c => c.Type == "email").Value;
        return Task.FromResult(new ExternalUser(id, displayName, email));
    }

    public async Task<IEnumerable<ExternalUser>> FindUsers(string query, CancellationToken ct)
    => await dataNoseApiClient.SearchPeople(query, ct);
}