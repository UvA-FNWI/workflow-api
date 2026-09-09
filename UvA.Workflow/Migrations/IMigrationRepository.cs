namespace UvA.Workflow.Migrations;

public interface IMigrationRepository
{
    Task<IReadOnlyList<Migration>> GetAll(CancellationToken ct = default);
    Task<Migration?> GetByMigrationId(string migrationId, CancellationToken ct = default);
    Task Create(Migration migration, CancellationToken ct = default);
    Task Update(Migration migration, CancellationToken ct = default);

    Task<PropertyRenameResult> RenamePropertyValues(Migration migration, CancellationToken ct = default);

    Task<long> RenameJournalPaths(Migration migration, CancellationToken ct = default);
}

public record PropertyRenameResult(long InstancesMatched, long InstancesUpdated);