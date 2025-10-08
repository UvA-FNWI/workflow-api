namespace UvA.Workflow.Users;

public class UserService(IUserRepository userRepository)
{
    /// <summary>
    /// Gets a user by their external ID.
    /// </summary>
    public Task<User?> GetByExternalIdAsync(string externalId)
        => userRepository.GetByExternalIdAsync(externalId);

    /// <summary>
    /// Gets multiple users by their IDs.
    /// </summary>
    public Task<IEnumerable<User>> GetByIdsAsync(IReadOnlyList<string> ids)
        => userRepository.GetByIdsAsync(ids);

    /// <summary>
    /// Creates a new user.
    /// </summary>
    public Task CreateAsync(User user)
        => userRepository.CreateAsync(user);

    /// <summary>
    /// Updates an existing user.
    /// </summary>
    public Task UpdateAsync(User user)
        => userRepository.UpdateAsync(user);

    /// <summary>
    /// Gets a user by their ID.
    /// </summary>
    public Task<User?> GetByIdAsync(string id)
        => userRepository.GetByIdAsync(id);
}