using System.Security.Claims;

namespace UvA.Workflow.Users;

public interface IUserService
{
    Task<GlobalRole[]> GetRoles(ClaimsPrincipal principal);
    ExternalUser? GetUserInfo(ClaimsPrincipal principal);
    Task<IEnumerable<ExternalUser>> FindUsers(string query, CancellationToken cancellationToken);
    Task<User?> GetCurrentUser(CancellationToken ct = default);
}