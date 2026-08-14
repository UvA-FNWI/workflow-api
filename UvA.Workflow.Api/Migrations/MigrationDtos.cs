using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

public record CreateMigrationDto(
    string Name,
    MigrationKind Kind,
    string WorkflowDefinition,
    string OldPath,
    string NewPath,
    string TargetRef,
    string? Description);

public record MigrationDto(
    string Id,
    MigrationKind Kind,
    string WorkflowDefinition,
    string OldPath,
    string NewPath,
    string? Description,
    string SourceCommit,
    string TargetCommit,
    MigrationStage Stage,
    string StageLabel,
    MigrationRunStatus RunStatus,
    string RunStatusLabel,
    MigrationCheckpoint Checkpoint,
    MigrationCounts Counts,
    string? AttentionReason)
{
    public static MigrationDto Create(Migration migration)
        => new(
            migration.Id,
            migration.Kind,
            migration.WorkflowDefinition,
            migration.OldPath,
            migration.NewPath,
            migration.Description,
            migration.SourceCommit,
            migration.TargetCommit,
            migration.Stage,
            StageName(migration.Stage),
            migration.RunStatus,
            RunStatusName(migration.RunStatus, migration.Stage),
            migration.Checkpoint,
            migration.Counts,
            migration.AttentionReason);

    private static string StageName(MigrationStage stage) => stage switch
    {
        MigrationStage.Planned => "Nothing changed yet",
        MigrationStage.SupportingBothNames => "Keeping old and new fields in sync",
        MigrationStage.CopyingInstances => "Copying existing instance data",
        MigrationStage.UpdatingHistory => "Updating property history",
        MigrationStage.CheckingResults => "Checking the migrated data",
        MigrationStage.UsingNewName => "Using the new name",
        MigrationStage.RemovingOldName => "Removing the old MongoDB field",
        MigrationStage.Finished => "Rename complete",
        _ => stage.ToString()
    };

    private static string RunStatusName(MigrationRunStatus status, MigrationStage stage) => status switch
    {
        MigrationRunStatus.Waiting when stage == MigrationStage.Planned => "Waiting for approval",
        MigrationRunStatus.Waiting when stage == MigrationStage.SupportingBothNames =>
            "Ready to copy existing data",
        MigrationRunStatus.Waiting when stage == MigrationStage.CopyingInstances =>
            "Waiting to continue copying instance data",
        MigrationRunStatus.Waiting when stage == MigrationStage.UpdatingHistory =>
            "Waiting to continue updating property history",
        MigrationRunStatus.Waiting when stage == MigrationStage.CheckingResults =>
            "Waiting to check the migrated data",
        MigrationRunStatus.Waiting when stage == MigrationStage.UsingNewName =>
            "Ready to remove the old MongoDB field",
        MigrationRunStatus.Waiting when stage == MigrationStage.RemovingOldName =>
            "Waiting to continue removing the old MongoDB field",
        MigrationRunStatus.Waiting => "Waiting for the next step",
        MigrationRunStatus.Running => "In progress",
        MigrationRunStatus.Paused => "Paused",
        MigrationRunStatus.NeedsAttention => "Needs attention",
        MigrationRunStatus.Done => "Complete",
        _ => status.ToString()
    };
}

public record MigrationOverviewDto(
    VersionInfo? ActiveConfiguration,
    PendingBaselineInfo? PendingConfiguration,
    IReadOnlyList<MigrationDto> Migrations);

public record CreateMigrationResponseDto(
    string PendingCommit,
    string Message,
    MigrationDto Migration);