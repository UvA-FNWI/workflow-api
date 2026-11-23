namespace UvA.Workflow.Users.DataNose;

public class DataNoseUserSearchSource(IDataNoseApiClient dataNoseApiClient) : IUserSearchSource
{
    public Task<IEnumerable<UserSearchResult>> FindUsers(string query, UserSearchScope? scope = null,
        CancellationToken ct = default)
        => dataNoseApiClient.SearchPeople(query, scope, ct);

    public async Task<UserSearchResult?> FindUser(string userName, UserSearchScope? scope = null,
        CancellationToken ct = default)
    {
        var user = (await FindUsers(userName, scope, ct)).FirstOrDefault(MatchesUserName);
        if (user != null || scope?.IncludeStudents != true ||
            userName is not { Length: 7 } || userName[0] != '0' || !userName.All(char.IsAsciiDigit))
            return user;

        // DataNose indexes the raw SIS ID, while Workflow usernames are zero-padded.
        var studentId = userName.TrimStart('0');
        if (studentId.Length == 0)
            return null;

        return (await FindUsers(studentId, new UserSearchScope(false, true), ct)).FirstOrDefault(MatchesUserName);

        bool MatchesUserName(UserSearchResult candidate) =>
            string.Equals(candidate.UserName, userName, StringComparison.OrdinalIgnoreCase);
    }
}