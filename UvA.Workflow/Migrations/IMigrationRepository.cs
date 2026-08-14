namespace UvA.Workflow.Migrations;

public interface IMigrationRepository
{
    Task<IReadOnlyList<Migration>> GetAll(CancellationToken ct = default);
    Task<Migration?> GetById(string id, CancellationToken ct = default);
    Task Create(Migration migration, CancellationToken ct = default);
    Task Update(Migration migration, CancellationToken ct = default);
    Task<long> CountTargetFields(Migration migration, CancellationToken ct = default);

    Task<PropertyCopyResult> CopyPropertyValues(Migration migration, bool overwriteTarget,
        CancellationToken ct = default);

    Task<long> RemoveTargetFields(Migration migration, CancellationToken ct = default);
    Task<long> RenameJournalPaths(Migration migration, CancellationToken ct = default);
}

public record PropertyCopyResult(long InstancesMatched, long InstancesUpdated);