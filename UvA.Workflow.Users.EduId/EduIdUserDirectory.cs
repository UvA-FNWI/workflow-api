namespace UvA.Workflow.Users.EduId;

public class EduIdUserDirectory(IUserRepository userRepository) : IUserRoleSource, IUserSearchSource
{
    public string ProviderKey => EduIdDirectoryKeys.ProviderKey;
    public string SourceKey => EduIdDirectoryKeys.SourceKey;

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<string>());

    public async Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken ct = default)
        => (await userRepository.SearchByQuery(query, EduIdDirectoryKeys.ProviderKey, ct))
            .Select(user => new UserSearchResult(user.UserName,
                user.DisplayName,
                user.Email,
                EduIdDirectoryKeys.SourceKey))
            .ToArray();
}
