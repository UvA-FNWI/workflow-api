using Microsoft.Extensions.Caching.Memory;

namespace UvA.Workflow.Users;

public class MockUserService(IUserRepository userRepository, IMemoryCache cache)
    : UserServiceBase(userRepository, cache), IUserService
{
    private static readonly IEnumerable<User> DummyUsers =
    [
        new() { UserName = "1", DisplayName = "User 1", Email = "1@invalid.invalid" },
        new() { UserName = "2", DisplayName = "User 2", Email = "2@invalid.invalid" },
        new() { UserName = "3", DisplayName = "User 3", Email = "3@invalid.invalid" }
    ];

    private static readonly IEnumerable<string> Roles = ["Coordinator", "Api", "Admin"];

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default) => Task.FromResult(Roles);

    public Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken cancellationToken)
        => Task.FromResult(DummyUsers
            .Where(u => u.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(r => new UserSearchResult(r.Id, r.DisplayName, r.Email)));

    public async Task<User?> GetCurrentUser(CancellationToken ct = default)
    {
        var user = DummyUsers.First();
        return await AddOrUpdateUser(user.UserName, user.DisplayName, user.Email, ct);
    }

    public Task<IEnumerable<string>> GetRolesOfCurrentUser(CancellationToken ct = default) => Task.FromResult(Roles);
}