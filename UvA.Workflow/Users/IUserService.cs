using System.Security.Claims;

namespace UvA.Workflow.Users;

public interface IUserService
{
    Task<GlobalRole[]> GetRoles(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<ExternalUser> GetUserInfo(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<IEnumerable<ExternalUser>> FindUsers(string query, CancellationToken ct);
}