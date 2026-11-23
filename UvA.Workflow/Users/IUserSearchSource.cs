namespace UvA.Workflow.Users;

public interface IUserSearchSource
{
    Task<IEnumerable<UserSearchResult>> FindUsers(string query, UserSearchScope? scope = null,
        CancellationToken ct = default);
}