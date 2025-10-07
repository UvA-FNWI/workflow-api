using System.Security.Claims;

namespace Uva.Workflow.Users;

public interface IUserService
{
    Task<GlobalRole[]> GetRoles(ClaimsPrincipal principal);
    ExternalUser? GetUserInfo(ClaimsPrincipal principal);
}
