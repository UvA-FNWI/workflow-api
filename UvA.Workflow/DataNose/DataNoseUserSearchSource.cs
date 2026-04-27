namespace UvA.Workflow.DataNose;

public class DataNoseUserSearchSource(IDataNoseApiClient dataNoseApiClient) : IUserSearchSource
{
    public Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken ct = default)
        => dataNoseApiClient.SearchPeople(query, ct);
}