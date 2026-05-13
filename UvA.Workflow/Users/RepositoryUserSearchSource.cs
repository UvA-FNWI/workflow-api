namespace UvA.Workflow.Users;

public class RepositoryUserSearchSource(IUserRepository userRepository) : IUserSearchSource
{
    public async Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken ct = default)
        => (await userRepository.SearchByQuery(query, ct))
            .Select(CreateSearchResult)
            .ToArray();

    private static UserSearchResult CreateSearchResult(User user) => new(
        user.UserName,
        user.DisplayName,
        user.Email,
        UserSearchSources.Repository,
        user.ProviderKey,
        user.Organization);
}