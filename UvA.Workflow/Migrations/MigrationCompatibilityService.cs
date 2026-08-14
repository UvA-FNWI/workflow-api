namespace UvA.Workflow.Migrations;

/// <summary>
/// Resolves durable rename mappings independently of whichever YAML version a request is using.
/// This is what lets an old and a new application version share the same MongoDB documents safely.
/// </summary>
public class MigrationCompatibilityService(IMigrationStore migrationStore)
{
    private Task<IReadOnlyList<Migration>>? _migrations;

    public async Task<IReadOnlyList<PropertyRenameAlias>> GetAliases(
        string? workflowDefinition = null,
        CancellationToken ct = default)
    {
        var migrations = await (_migrations ??= migrationStore.GetAll(ct));
        return migrations
            .Where(migration => migration.Kind == MigrationKind.RenameProperty)
            .Where(migration => migration.Stage >= MigrationStage.SupportingBothNames &&
                                migration.Stage < MigrationStage.RemovingOldName)
            .Where(migration => workflowDefinition == null ||
                                migration.WorkflowDefinition == workflowDefinition)
            .Select(migration => new PropertyRenameAlias(
                migration.WorkflowDefinition,
                migration.OldPath,
                migration.NewPath))
            .ToArray();
    }

    public async Task Attach(WorkflowInstance instance, CancellationToken ct = default)
    {
        instance.PropertyRenameAliases = await GetAliases(instance.WorkflowDefinition, ct);
        instance.MaterializeMissingPropertyAliases();
    }

    public async Task Attach(IEnumerable<WorkflowInstance> instances, CancellationToken ct = default)
    {
        foreach (var instance in instances)
            await Attach(instance, ct);
    }
}

public record PropertyRenameAlias(
    string WorkflowDefinition,
    string OldPath,
    string NewPath);