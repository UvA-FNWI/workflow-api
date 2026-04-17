namespace UvA.Workflow.Users.DataNose;

public class DataNoseUserSearchSource(IDataNoseApiClient dataNoseApiClient) : IUserSearchSource
{
    public string SourceKey => DataNoseDirectoryKeys.SourceKey;

    public Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken ct = default)
        => dataNoseApiClient.SearchPeople(query, ct);
}
