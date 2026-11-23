namespace UvA.Workflow.Users.DataNose;

public class DataNoseUserSearchSource(IDataNoseApiClient dataNoseApiClient) : IUserSearchSource
{
    public Task<IEnumerable<UserSearchResult>> FindUsers(string query, UserSearchScope? scope = null,
        CancellationToken ct = default)
        => dataNoseApiClient.SearchPeople(query, scope, ct);
}