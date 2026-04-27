namespace UvA.Workflow.Users;

public interface IUserSearchSource
{
    Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken ct = default);
}