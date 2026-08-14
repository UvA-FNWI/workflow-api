namespace UvA.Workflow.Migrations;

/// <summary>Persistence contract for globally active property migrations.</summary>
public interface IMigrationStore
{
    Task<IReadOnlyList<Migration>> GetAll(CancellationToken ct = default);
    Task<Migration?> GetById(string id, CancellationToken ct = default);

    /// <summary>
    /// Publishes a migration for runtime compatibility. Repeating the same request is safe; reusing an id
    /// for a different definition is rejected.
    /// </summary>
    Task<Migration> EnsureCreated(Migration migration, CancellationToken ct = default);
}