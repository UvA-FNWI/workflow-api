using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

public record CreatePropertyRenameDto(
    string[] WorkflowDefinitions,
    string OldProperty,
    string NewProperty);

public record MigrationDto(
    string MigrationId,
    MigrationKind Kind,
    MigrationStatus Status,
    string StatusLabel,
    string[] WorkflowDefinitions,
    string OldProperty,
    string NewProperty,
    string RequestedBy,
    DateTime RequestedAt,
    DateTime UpdatedAt,
    DateTime? FinishedAt,
    long ItemsMatched,
    long ItemsUpdated,
    long JournalEntriesUpdated,
    string? Error)
{
    public static MigrationDto Create(Migration migration) => new(
        migration.MigrationId,
        migration.Kind,
        migration.Status,
        migration.Status switch
        {
            MigrationStatus.Applying => "Applying the property rename",
            MigrationStatus.Finished => "Finished",
            MigrationStatus.Failed => "Failed",
            _ => migration.Status.ToString()
        },
        migration.WorkflowDefinitions,
        migration.OldProperty,
        migration.NewProperty,
        migration.RequestedBy,
        migration.RequestedAt,
        migration.UpdatedAt,
        migration.FinishedAt,
        migration.ItemsMatched,
        migration.ItemsUpdated,
        migration.JournalEntriesUpdated,
        migration.Error);
}