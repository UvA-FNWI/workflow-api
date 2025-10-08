namespace UvA.Workflow.Users;

/// <summary>
/// Repository contract for User persistence.
/// Defines the interface without implementation details.
/// </summary>
public interface IUserRepository
{
    // Core CRUD
    Task Create(User user, CancellationToken ct);
    Task<User?> GetById(string id, CancellationToken ct);
    Task Update(User user, CancellationToken ct);

    // Query operations
    Task<User?> GetByExternalId(string externalId, CancellationToken ct);
    Task<IEnumerable<User>> GetByIds(IReadOnlyList<string> ids, CancellationToken ct);
}