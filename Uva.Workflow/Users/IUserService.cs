using System.Security.Claims;

namespace UvA.Workflow.Users;

public interface IUserService
{
    Task<GlobalRole[]> GetRoles(ClaimsPrincipal principal);
    ExternalUser? GetUserInfo(ClaimsPrincipal principal);
}