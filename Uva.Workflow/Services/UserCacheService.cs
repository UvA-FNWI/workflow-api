using System.Collections.Concurrent;

namespace UvA.Workflow.Services;

public class UserCacheService(UserService userService)
{
    private readonly ConcurrentDictionary<string, User> _dictionary = new();

    public User? GetUser(string id) => _dictionary.GetValueOrDefault(id);
    private void AddUser(string id, User user) => _dictionary.TryAdd(id, user);

    public async Task<User> GetUser(ExternalUser extUser)
    {
        var user = GetUser(extUser.Id);
        if (user != null)
            return user;

        user = await userService.GetByExternalIdAsync(extUser.Id)
               ?? new User { ExternalId = extUser.Id, DisplayName = extUser.DisplayName, Email = extUser.Email };
        if (user.Id == null!)
            await userService.CreateAsync(user);
        else if (user.DisplayName != extUser.DisplayName || user.Email != extUser.Email)
        {
            user.Email = extUser.Email;
            user.DisplayName = extUser.DisplayName;
            await userService.UpdateAsync(user);
        }

        AddUser(extUser.Id, user);
        return user;
    }
}