namespace UvA.Workflow.Api.Authentication;

public class EduIdUserDirectory(IUserRepository userRepository) : IUserRoleSource, IUserSearchSource
{
    public bool CanResolve(User user) => user.AuthProvider == UserAuthProvider.EduId;

    public Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
        => Task.FromResult(Enumerable.Empty<string>());

    public async Task<IEnumerable<UserSearchResult>> FindUsers(string query, CancellationToken ct = default)
        => (await userRepository.SearchByQuery(query, UserAuthProvider.EduId, ct))
            .Select(user => new UserSearchResult(user.UserName, user.DisplayName, user.Email))
            .ToArray();
}