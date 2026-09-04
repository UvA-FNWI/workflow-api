using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Migrations;

public class MigrationService(
    ModelService modelService,
    IMigrationRepository migrationRepository)
{
    public Task<IReadOnlyList<Migration>> GetAll(CancellationToken ct = default)
        => migrationRepository.GetAll(ct);

    public async Task<Migration> CreatePropertyRename(
        IEnumerable<string>? workflowDefinitions,
        string oldProperty,
        string newProperty,
        string requestedBy,
        CancellationToken ct = default)
    {
        var migration = new Migration
        {
            MigrationId = ObjectId.GenerateNewId().ToString(),
            Kind = MigrationKind.RenameProperty,
            WorkflowDefinitions = NormalizeWorkflows(workflowDefinitions),
            OldProperty = oldProperty,
            NewProperty = newProperty
        };
        await Prepare(migration, requestedBy, ct);
        await migrationRepository.Create(migration, ct);
        return await Execute(migration, ct);
    }

    public async Task<Migration> RunConfigured(ConfiguredMigration configured, CancellationToken ct = default)
    {
        var existing = await migrationRepository.GetByMigrationId(configured.MigrationId, ct);
        if (existing != null)
            return existing;

        var migration = new Migration
        {
            MigrationId = configured.MigrationId,
            Kind = configured.Kind,
            WorkflowDefinitions = [configured.WorkflowDefinition],
            OldProperty = configured.OldProperty,
            NewProperty = configured.NewProperty
        };
        await Prepare(migration, "configuration", ct);
        await migrationRepository.Create(migration, ct);
        return await Execute(migration, ct);
    }

    private async Task Prepare(
        Migration migration,
        string requestedBy,
        CancellationToken ct)
    {
        if (migration.WorkflowDefinitions.Length == 0)
            throw new MigrationValidationException("MigrationWorkflowRequired",
                "At least one workflow is required");

        if (migration.Kind != MigrationKind.RenameProperty)
            throw new MigrationValidationException("UnsupportedMigrationKind",
                $"Migration kind '{migration.Kind}' is not supported");
        ValidatePropertyName(migration.OldProperty, nameof(Migration.OldProperty));
        ValidatePropertyName(migration.NewProperty, nameof(Migration.NewProperty));
        if (migration.OldProperty == migration.NewProperty)
            throw new MigrationValidationException("MigrationPropertiesMustDiffer",
                "The old and new property names must be different");

        foreach (var workflow in migration.WorkflowDefinitions)
        {
            if (!modelService.WorkflowDefinitions.TryGetValue(workflow, out var definition))
                throw new MigrationValidationException("MigrationUnknownWorkflow",
                    $"Unknown workflow '{workflow}'");

            var hasOldProperty = definition.Properties.Contains(migration.OldProperty);
            var hasNewProperty = definition.Properties.Contains(migration.NewProperty);
            if (hasOldProperty == hasNewProperty)
                throw new MigrationValidationException("MigrationInvalidModelState",
                    $"Workflow '{workflow}' must contain exactly one of '{migration.OldProperty}' and '{migration.NewProperty}'");
        }

        var requestedProperties = new HashSet<string>([migration.OldProperty, migration.NewProperty],
            StringComparer.Ordinal);
        var active = await migrationRepository.GetAll(ct);
        var conflictingProperties = active
            .Where(value => value.MigrationId != migration.MigrationId && value.Status != MigrationStatus.Finished)
            .SelectMany(activeMigration => activeMigration.WorkflowDefinitions
                .Intersect(migration.WorkflowDefinitions, StringComparer.Ordinal)
                .SelectMany(workflow => new[] { activeMigration.OldProperty, activeMigration.NewProperty }
                    .Intersect(requestedProperties, StringComparer.Ordinal)
                    .Select(property => $"{workflow}.{property}")))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (conflictingProperties.Length > 0)
            throw new MigrationValidationException("MigrationPropertyOverlap",
                $"The following workflow properties already have an unfinished migration: {string.Join(", ", conflictingProperties)}");

        var now = DateTime.UtcNow;
        migration.Status = MigrationStatus.Applying;
        migration.RequestedBy = requestedBy;
        migration.RequestedAt = now;
        migration.UpdatedAt = now;
    }

    private async Task<Migration> Execute(Migration migration, CancellationToken ct)
    {
        migration.Status = MigrationStatus.Applying;
        migration.Error = null;
        migration.UpdatedAt = DateTime.UtcNow;

        try
        {
            var result = await migrationRepository.CopyPropertyValues(migration, ct);
            migration.ItemsMatched = result.InstancesMatched;
            migration.ItemsUpdated = result.InstancesUpdated;
            migration.JournalEntriesUpdated = await migrationRepository.RenameJournalPaths(migration, ct);
            migration.Status = MigrationStatus.Finished;
            migration.FinishedAt = migration.UpdatedAt = DateTime.UtcNow;
            await migrationRepository.Update(migration, ct);
            return migration;
        }
        catch (Exception exception)
        {
            await SaveFailure(migration, exception, ct);
            throw;
        }
    }

    private async Task SaveFailure(Migration migration, Exception exception, CancellationToken ct)
    {
        migration.Status = MigrationStatus.Failed;
        migration.Error = exception.Message;
        migration.UpdatedAt = DateTime.UtcNow;
        await migrationRepository.Update(migration, ct);
    }

    private static string[] NormalizeWorkflows(IEnumerable<string>? workflowDefinitions)
        => (workflowDefinitions ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void ValidatePropertyName(string property, string field)
    {
        if (string.IsNullOrWhiteSpace(property) || property.Contains('.') || property.StartsWith('$'))
            throw new MigrationValidationException("InvalidMigrationProperty",
                $"{field} must be a top-level property name");
    }
}