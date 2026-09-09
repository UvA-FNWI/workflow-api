using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

public record MigrationDto(
    string MigrationId,
    string Scope,
    MigrationKind Kind,
    MigrationStatus Status,
    string[] WorkflowDefinitions,
    string OldProperty,
    string NewProperty,
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
        migration.Scope,
        migration.Kind,
        migration.Status,
        migration.WorkflowDefinitions,
        migration.OldProperty,
        migration.NewProperty,
        migration.RequestedAt,
        migration.UpdatedAt,
        migration.FinishedAt,
        migration.ItemsMatched,
        migration.ItemsUpdated,
        migration.JournalEntriesUpdated,
        migration.Error);
}