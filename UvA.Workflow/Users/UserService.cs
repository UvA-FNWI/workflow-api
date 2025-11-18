using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace UvA.Workflow.Users;

public class UserService(IHttpContextAccessor httpContextAccessor, IUserRepository userRepository, IMemoryCache cache)
    : IUserService
{
    private static TimeSpan UserCacheExpiration => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Retrieves the current authenticated user from the HTTP context or cache. If the user is not present in cache, it retrieves the user from the repository and caches the result for a specified duration.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>A <see cref="User"/> object representing the current user if authenticated, or null if the user is not authenticated or not found.</returns>
    public async Task<User?> GetCurrentUser(CancellationToken ct = default)
    {
        var principal = httpContextAccessor.HttpContext.User;
        if (!(principal?.Identity?.IsAuthenticated ?? false)) return null;
        var name = principal.Identity.Name!;
        var cacheKey = GetUserCacheKey(name);
        if (!cache.TryGetValue(cacheKey, out User? user))
        {
            user = await userRepository.GetByExternalId(name, ct);
            if (user == null) return null;
            cache.Set(cacheKey, user, UserCacheExpiration);
        }

        return user;
    }

    private static string GetUserCacheKey(string userName) => $"user:{userName}";


    // The functions below are implemented in unmerged branch DN-3384-Global-Roles

    public Task<GlobalRole[]> GetRoles(ClaimsPrincipal principal) => throw new NotImplementedException();

    public ExternalUser? GetUserInfo(ClaimsPrincipal principal) => throw new NotImplementedException();

    public Task<IEnumerable<ExternalUser>> FindUsers(string query, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}