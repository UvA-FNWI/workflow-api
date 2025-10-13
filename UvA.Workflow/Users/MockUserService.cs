using System.Security.Claims;

namespace UvA.Workflow.Users;

public class MockUserService : IUserService
{
    public Task<GlobalRole[]> GetRoles(ClaimsPrincipal principal)
    {
        return Task.FromResult<GlobalRole[]>([
            new GlobalRole("Coordinator"),
            new GlobalRole("SuperAdmin"),
            new GlobalRole("Admin")
        ]);
    }

    public ExternalUser? GetUserInfo(ClaimsPrincipal principal)
    {
        return new ExternalUser("1", "User 1", "1@invalid.invalid");
    }
}