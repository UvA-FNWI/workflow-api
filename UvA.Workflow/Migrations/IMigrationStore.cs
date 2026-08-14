namespace UvA.Workflow.Migrations;

/// <summary>Persistence contract for globally active property migrations.</summary>
public interface IMigrationStore
{
    Task<IReadOnlyList<Migration>> GetAll(CancellationToken ct = default);
    Task<Migration?> GetById(string id, CancellationToken ct = default);
}