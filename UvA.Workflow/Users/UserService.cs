using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using UvA.Workflow.DataNose;

namespace UvA.Workflow.Users;

public class UserService(
    IHttpContextAccessor httpContextAccessor,
    IDataNoseApiClient dataNoseApiClient,
    IUserRepository userRepository,
    IMemoryCache cache)
    : IUserService
{
    private static TimeSpan UserCacheExpiration => TimeSpan.FromMinutes(15);
    private static TimeSpan RolesCacheExpiration => TimeSpan.FromMinutes(15);
    private static string GetCacheKeyForUser(string userName) => $"user:{userName}";
    private static string GetCacheKeyForRoles(string userName) => $"roles:{userName}";

    /// <summary>
    /// Retrieves the current authenticated user from the HTTP context or cache. If the user is not present in cache, it retrieves the user from the repository and caches the result for a specified duration.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>A <see cref="User"/> object representing the current user if authenticated, or null if the user is not authenticated or not found.</returns>
    public async Task<User?> GetCurrentUser(CancellationToken ct = default)
    {
        var principal = httpContextAccessor.HttpContext.User;
        if (!(principal?.Identity?.IsAuthenticated ?? false)) return null;
        return await GetUser(principal.Identity.Name!, ct);
    }


    public async Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default)
    {
        var cacheKey = GetCacheKeyForRoles(user.UserName);
        if (cache.TryGetValue(cacheKey, out string[]? roles)) return roles!;
        roles = (await dataNoseApiClient.GetRolesByUser(user.UserName, ct)).ToArray();
        cache.Set(cacheKey, roles, RolesCacheExpiration);
        return roles;
    }

    /// <summary>
    /// Retrieves the roles of the current authenticated user. If the user is not authenticated or not found, returns an empty collection.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>An enumerable collection of strings representing the roles assigned to the current user, or an empty collection if the user is not authenticated or roles cannot be retrieved.</returns>
    public async Task<IEnumerable<string>> GetRolesOfCurrentUser(CancellationToken ct = default)
    {
        var user = await GetCurrentUser(ct);
        return user is null ? [] : await GetRoles(user, ct);
    }

    public async Task<IEnumerable<UserInfo>> FindUsers(string query, CancellationToken ct)
        => await dataNoseApiClient.SearchPeople(query, ct);

    /// <summary>
    /// Adds a new user or updates an existing user in the repository. If the user does not exist,
    /// it creates a new user with the provided details. If the user exists, it updates the user's
    /// information if any changes are detected. The result is cached for a specified duration.
    /// </summary>
    /// <param name="username">A string representing the unique external identifier for the user.</param>
    /// <param name="displayName">A string representing the display name of the user.</param>
    /// <param name="email">A string containing the email address of the user.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>A <see cref="User"/> object representing the added or updated user.</returns>
    public async Task<User> AddOrUpdateUser(string username, string displayName, string email, CancellationToken ct)
    {
        var cacheKey = GetCacheKeyForUser(username);
        if (!cache.TryGetValue(cacheKey, out User? user))
        {
            user = await userRepository.GetByExternalId(username, ct);
        }

        if (user == null)
        {
            user = new User { UserName = username, DisplayName = displayName, Email = email };
            await userRepository.Create(user, ct);
        }
        else
        {
            var changed = false;
            if (user.DisplayName != displayName)
            {
                changed = true;
                user.DisplayName = displayName;
            }

            if (user.Email != email)
            {
                changed = true;
                user.Email = email;
            }

            if (changed) await userRepository.Update(user, ct);
        }

        cache.Set(cacheKey, user, UserCacheExpiration);
        return user;
    }

    /// <summary>
    /// Retrieves a user by their username from the cache, or the user repository if not cached. If the user is found in the repository, it is added to the cache for future requests.
    /// </summary>
    /// <param name="username">The unique username of the user to retrieve.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>A <see cref="User"/> object matching the specified username if found, or null if no such user exists.</returns>
    public async Task<User?> GetUser(string username, CancellationToken ct)
    {
        var cacheKey = GetCacheKeyForUser(username);
        if (cache.TryGetValue(cacheKey, out User? user)) return user;
        user = await userRepository.GetByExternalId(username, ct);
        if (user != null)
            cache.Set(cacheKey, user, UserCacheExpiration);
        return user;
    }
}