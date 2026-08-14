using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

public record CreatePropertyRenameDto(
    string Name,
    string WorkflowDefinition,
    string OldProperty,
    string NewProperty,
    string? Description);

public record MigrationDto(
    string Id,
    string Name,
    MigrationKind Kind,
    MigrationStatus Status,
    string StatusLabel,
    object Definition,
    string? Description,
    string RequestedBy,
    DateTime RequestedAt,
    DateTime UpdatedAt,
    DateTime? FinishedAt,
    MigrationProgress Progress,
    string? Error)
{
    public static MigrationDto Create(Migration migration) => new(
        migration.Id,
        migration.Name,
        migration.Kind,
        migration.Status,
        migration.Status switch
        {
            MigrationStatus.Applying => "Applying the property rename",
            MigrationStatus.ReadyToFinish => "Ready to finish",
            MigrationStatus.Finishing => "Finishing the rename",
            MigrationStatus.Finished => "Finished",
            MigrationStatus.Reverting => "Reverting the copied property",
            MigrationStatus.Reverted => "Reverted",
            MigrationStatus.ApplyFailed => "Applying the property rename failed",
            MigrationStatus.FinishFailed => "Finish failed",
            MigrationStatus.RevertFailed => "Revert failed",
            _ => migration.Status.ToString()
        },
        migration.Definition,
        migration.Description,
        migration.RequestedBy,
        migration.RequestedAt,
        migration.UpdatedAt,
        migration.FinishedAt,
        migration.Progress,
        migration.Error);
}