namespace UvA.Workflow.Users;

public interface IUserSearchSource
{
    Task<IEnumerable<UserSearchResult>> FindUsers(string query, UserSearchScope? scope = null,
        CancellationToken ct = default);

    async Task<UserSearchResult?> FindUser(string userName, UserSearchScope? scope = null,
        CancellationToken ct = default)
        => (await FindUsers(userName, scope, ct)).FirstOrDefault(user =>
            string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase));
}