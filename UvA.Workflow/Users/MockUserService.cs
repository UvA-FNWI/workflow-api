namespace UvA.Workflow.Users;

public class MockUserService : IUserService
{
    private static readonly IEnumerable<User> DummyUsers =
    [
        new() { Id = "1", DisplayName = "User 1", Email = "1@invalid.invalid" },
        new() { Id = "2", DisplayName = "User 2", Email = "2@invalid.invalid" },
        new() { Id = "3", DisplayName = "User 3", Email = "3@invalid.invalid" }
    ];

    private static readonly IEnumerable<string> Roles = ["Coordinator", "SuperAdmin", "Admin"];

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken cancellationToken)
        => Task.FromResult(DummyUsers
            .Where(u => u.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(r => new UserSearchResult(r.Id, r.DisplayName, r.Email)));

    public Task<User?> GetCurrentUser(CancellationToken ct = default) => Task.FromResult(DummyUsers.FirstOrDefault());

    public Task<IEnumerable<string>> GetRolesOfCurrentUser(CancellationToken ct = default) => Task.FromResult(Roles);

    public Task<User>
        AddOrUpdateUser(string username, string displayName, string email, CancellationToken ct = default) =>
        GetCurrentUser(ct)!;

    public Task<User?> GetUser(string username, CancellationToken ct) =>
        Task.FromResult(DummyUsers.FirstOrDefault(u => u.Id == username));
}