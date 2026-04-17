namespace UvA.Workflow.Users;

public interface IUserSearchSource
{
    string SourceKey { get; }

    Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken ct = default);
}