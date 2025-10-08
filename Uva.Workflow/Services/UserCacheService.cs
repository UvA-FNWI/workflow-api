using System.Collections.Concurrent;

namespace UvA.Workflow.Services;

public class UserCacheService(IUserRepository userRepository)
{
    private readonly ConcurrentDictionary<string, User> _dictionary = new();

    public User? GetUser(string id) => _dictionary.GetValueOrDefault(id);
    private void AddUser(string id, User user) => _dictionary.TryAdd(id, user);

    public async Task<User> GetUser(ExternalUser extUser, CancellationToken ct)
    {
        var user = GetUser(extUser.Id);
        if (user != null)
            return user;

        user = await userRepository.GetByExternalId(extUser.Id, ct)
               ?? new User { ExternalId = extUser.Id, DisplayName = extUser.DisplayName, Email = extUser.Email };
        if (user.Id == null!)
            await userRepository.Create(user, ct);
        else if (user.DisplayName != extUser.DisplayName || user.Email != extUser.Email)
        {
            user.Email = extUser.Email;
            user.DisplayName = extUser.DisplayName;
            await userRepository.Update(user, ct);
        }

        AddUser(extUser.Id, user);
        return user;
    }
}