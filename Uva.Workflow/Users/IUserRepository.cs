namespace Uva.Workflow.Users;

/// <summary>
/// Repository contract for User persistence.
/// Defines the interface without implementation details.
/// </summary>
public interface IUserRepository
{
    // Core CRUD
    Task CreateAsync(User user);
    Task<User?> GetByIdAsync(string id);
    Task UpdateAsync(User user);

    // Query operations
    Task<User?> GetByExternalIdAsync(string externalId);
    Task<IEnumerable<User>> GetByIdsAsync(IReadOnlyList<string> ids);
}

