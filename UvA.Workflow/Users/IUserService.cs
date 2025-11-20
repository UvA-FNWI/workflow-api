using UvA.Workflow.DataNose;

namespace UvA.Workflow.Users;

public interface IUserService
{
    Task<IEnumerable<string>> GetRoles(User user, CancellationToken ct = default);

    Task<IEnumerable<UserInfo>> FindUsers(string query, CancellationToken ct);

    /// <summary>
    /// Retrieves the current authenticated user from the HTTP context or cache. If the user is not present in cache, it retrieves the user from the repository and caches the result for a specified duration.
    /// </summary>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>A <see cref="User"/> object representing the current user if authenticated, or null if the user is not authenticated or not found.</returns>
    Task<User?> GetCurrentUser(CancellationToken ct = default);

    Task<IEnumerable<string>> GetRolesOfCurrentUser(CancellationToken ct = default);

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
    Task<User> AddOrUpdateUser(string username, string displayName, string email, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a user by their username from the cache, or the user repository if not cached. If the user is found in the repository, it is added to the cache for future requests.
    /// </summary>
    /// <param name="username">The unique username of the user to retrieve.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> used to observe cancellation requests.</param>
    /// <returns>A <see cref="User"/> object matching the specified username if found, or null if no such user exists.</returns>
    Task<User?> GetUser(string username, CancellationToken ct);
}