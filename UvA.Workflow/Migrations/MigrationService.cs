using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Migrations;

public class MigrationService(
    ModelService modelService,
    IMigrationRepository migrationRepository)
{
    public Task<IReadOnlyList<Migration>> GetAll(CancellationToken ct = default)
        => migrationRepository.GetAll(ct);

    public async Task<Migration> CreatePropertyRename(
        string name,
        string workflowDefinition,
        string oldProperty,
        string newProperty,
        string? description,
        string requestedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Migration name is required");
        ValidatePropertyName(oldProperty, nameof(oldProperty));
        ValidatePropertyName(newProperty, nameof(newProperty));
        if (oldProperty == newProperty)
            throw new InvalidOperationException("The old and new property names must be different");

        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            throw new InvalidOperationException($"Unknown workflow '{workflowDefinition}'");
        if (definition.Properties.GetOrDefault(oldProperty) == null)
            throw new InvalidOperationException(
                $"Property '{oldProperty}' does not exist in workflow '{workflowDefinition}'");
        if (definition.Properties.GetOrDefault(newProperty) != null)
            throw new InvalidOperationException(
                $"Property '{newProperty}' already exists in workflow '{workflowDefinition}'");

        var active = await migrationRepository.GetAll(ct);
        if (active.Any(value => value.Definition is RenamePropertyDefinition rename &&
                                rename.WorkflowDefinition == workflowDefinition &&
                                value.Status is not (MigrationStatus.Finished or MigrationStatus.Reverted)))
            throw new InvalidOperationException(
                $"Workflow '{workflowDefinition}' already has an unfinished migration");

        var now = DateTime.UtcNow;
        var migration = new Migration
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Kind = MigrationKind.RenameProperty,
            Status = MigrationStatus.Applying,
            Name = name,
            Definition = new RenamePropertyDefinition
            {
                WorkflowDefinition = workflowDefinition,
                OldProperty = oldProperty,
                NewProperty = newProperty
            },
            Description = description,
            RequestedBy = requestedBy,
            RequestedAt = now,
            UpdatedAt = now
        };

        if (await migrationRepository.CountTargetFields(migration, ct) > 0)
            throw new InvalidOperationException(
                $"Some '{workflowDefinition}' instances already contain property '{newProperty}'");

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
            throw new InvalidOperationException($"Migration '{id}' is not ready to finish");

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
            throw new InvalidOperationException($"Migration '{id}' can no longer be reverted");

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
           ?? throw new InvalidOperationException($"Migration '{id}' does not exist");

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
            throw new InvalidOperationException($"{field} must be a top-level property name");
    }
}