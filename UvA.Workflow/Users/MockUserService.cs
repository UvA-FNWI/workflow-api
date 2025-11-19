using System.Security.Claims;

namespace UvA.Workflow.Users;

public class MockUserService : IUserService
{
    private static readonly IEnumerable<ExternalUser> DummyUsers =
    [
        new ExternalUser("1", "User 1", "1@invalid.invalid"),
        new ExternalUser("2", "User 2", "2@invalid.invalid"),
        new ExternalUser("3", "User 3", "3@invalid.invalid")
    ];

    public Task<GlobalRole[]> GetRoles(ClaimsPrincipal principal)
    {
        return Task.FromResult<GlobalRole[]>([
            new GlobalRole("Coordinator"),
            new GlobalRole("SuperAdmin"),
            new GlobalRole("Admin")
        ]);
    }

    public ExternalUser GetUserInfo(ClaimsPrincipal principal) => DummyUsers.First();

    public Task<IEnumerable<ExternalUser>> FindUsers(string query, CancellationToken cancellationToken)
        => Task.FromResult(DummyUsers.Where(u =>
            u.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

    public Task<User?> GetCurrentUser(CancellationToken ct) => Task.FromResult(DummyUsers.Select(eu => new User
    {
        DisplayName = eu.DisplayName, Id = ObjectId.GenerateNewId().ToString(), Email = eu.Email, ExternalId = eu.Id
    }).FirstOrDefault());
}