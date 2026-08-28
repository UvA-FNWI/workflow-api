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
        var workflows = (workflowDefinitions ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (workflows.Length == 0)
            throw new MigrationValidationException("MigrationWorkflowRequired",
                "At least one workflow is required");

        ValidatePropertyName(oldProperty, nameof(oldProperty));
        ValidatePropertyName(newProperty, nameof(newProperty));
        if (oldProperty == newProperty)
            throw new MigrationValidationException("MigrationPropertiesMustDiffer",
                "The old and new property names must be different");

        foreach (var workflow in workflows)
        {
            if (!modelService.WorkflowDefinitions.TryGetValue(workflow, out var definition))
                throw new MigrationValidationException("MigrationUnknownWorkflow",
                    $"Unknown workflow '{workflow}'");

            var hasOldProperty = definition.Properties.Contains(oldProperty);
            var hasNewProperty = definition.Properties.Contains(newProperty);
            if (hasOldProperty == hasNewProperty)
                throw new MigrationValidationException("MigrationInvalidModelState",
                    $"Workflow '{workflow}' must contain exactly one of '{oldProperty}' and '{newProperty}'");
        }

        var requestedProperties = new HashSet<string>([oldProperty, newProperty], StringComparer.Ordinal);
        var active = await migrationRepository.GetAll(ct);
        var conflictingProperties = active
            .Where(value => value.Status is not (MigrationStatus.Finished or MigrationStatus.Reverted))
            .Select(value => value.Definition)
            .OfType<RenamePropertyDefinition>()
            .SelectMany(rename => rename.WorkflowDefinitions
                .Intersect(workflows, StringComparer.Ordinal)
                .SelectMany(workflow => new[] { rename.OldProperty, rename.NewProperty }
                    .Intersect(requestedProperties, StringComparer.Ordinal)
                    .Select(property => $"{workflow}.{property}")))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (conflictingProperties.Length > 0)
            throw new MigrationValidationException("MigrationPropertyOverlap",
                $"The following workflow properties already have an unfinished migration: {string.Join(", ", conflictingProperties)}");

        var now = DateTime.UtcNow;
        var migration = new Migration
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Kind = MigrationKind.RenameProperty,
            Status = MigrationStatus.Applying,
            Definition = new RenamePropertyDefinition
            {
                WorkflowDefinitions = workflows,
                OldProperty = oldProperty,
                NewProperty = newProperty
            },
            RequestedBy = requestedBy,
            RequestedAt = now,
            UpdatedAt = now
        };

        if (await migrationRepository.CountTargetFields(migration, ct) > 0)
            throw new MigrationValidationException("MigrationTargetPropertyExists",
                $"Some selected workflow instances already contain property '{newProperty}'");

        await migrationRepository.Create(migration, ct);
        try
        {
            var result = await migrationRepository.CopyPropertyValues(migration, overwriteTarget: false, ct);
            migration.Progress.ItemsMatched = result.InstancesMatched;
            migration.Progress.ItemsUpdated = result.InstancesUpdated;
            migration.Status = MigrationStatus.ReadyToFinish;
            migration.UpdatedAt = DateTime.UtcNow;
            await migrationRepository.Update(migration, ct);
            return migration;
        }
        catch (Exception exception)
        {
            await SaveFailure(migration, MigrationStatus.ApplyFailed, exception, ct);
            throw;
        }
    }

    public async Task<Migration> Finish(string id, CancellationToken ct = default)
    {
        var migration = await Get(id, ct);
        if (migration.Status is not (MigrationStatus.ReadyToFinish or MigrationStatus.FinishFailed))
            throw new InvalidMigrationStateException($"Migration '{id}' is not ready to finish");

        migration.Status = MigrationStatus.Finishing;
        migration.Error = null;
        migration.UpdatedAt = DateTime.UtcNow;
        await migrationRepository.Update(migration, ct);
        try
        {
            var result = await migrationRepository.CopyPropertyValues(migration, overwriteTarget: true, ct);
            migration.Progress.ItemsMatched = result.InstancesMatched;
            migration.Progress.ItemsUpdated = result.InstancesUpdated;
            migration.Progress.Details["JournalEntriesUpdated"] =
                await migrationRepository.RenameJournalPaths(migration, ct);
            migration.Status = MigrationStatus.Finished;
            migration.FinishedAt = migration.UpdatedAt = DateTime.UtcNow;
            await migrationRepository.Update(migration, ct);
            return migration;
        }
        catch (Exception exception)
        {
            await SaveFailure(migration, MigrationStatus.FinishFailed, exception, ct);
            throw;
        }
    }

    public async Task<Migration> Revert(string id, CancellationToken ct = default)
    {
        var migration = await Get(id, ct);
        if (migration.Status is not (MigrationStatus.ReadyToFinish or MigrationStatus.ApplyFailed or
            MigrationStatus.RevertFailed))
            throw new InvalidMigrationStateException($"Migration '{id}' can no longer be reverted");

        migration.Status = MigrationStatus.Reverting;
        migration.Error = null;
        migration.UpdatedAt = DateTime.UtcNow;
        await migrationRepository.Update(migration, ct);
        try
        {
            await migrationRepository.RemoveTargetFields(migration, ct);
            migration.Status = MigrationStatus.Reverted;
            migration.UpdatedAt = DateTime.UtcNow;
            await migrationRepository.Update(migration, ct);
            return migration;
        }
        catch (Exception exception)
        {
            await SaveFailure(migration, MigrationStatus.RevertFailed, exception, ct);
            throw;
        }
    }

    private async Task<Migration> Get(string id, CancellationToken ct)
        => await migrationRepository.GetById(id, ct)
           ?? throw new MigrationNotFoundException(id);

    private async Task SaveFailure(Migration migration, MigrationStatus status, Exception exception,
        CancellationToken ct)
    {
        migration.Status = status;
        migration.Error = exception.Message;
        migration.UpdatedAt = DateTime.UtcNow;
        await migrationRepository.Update(migration, ct);
    }

    private static void ValidatePropertyName(string property, string field)
    {
        if (string.IsNullOrWhiteSpace(property) || property.Contains('.') || property.StartsWith('$'))
            throw new MigrationValidationException("InvalidMigrationProperty",
                $"{field} must be a top-level property name");
    }
}