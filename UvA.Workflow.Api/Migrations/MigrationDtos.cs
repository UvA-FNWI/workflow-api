using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

public record MigrationPlanDto(
    string Id,
    MigrationKind Kind,
    string WorkflowDefinition,
    string OldPath,
    string NewPath,
    string? Description,
    string Checksum)
{
    public static MigrationPlanDto Create(MigrationDefinition plan)
        => new(plan.Id, plan.MigrationKind, plan.WorkflowDefinition, plan.OldPath, plan.NewPath,
            plan.Description, plan.Checksum);
}

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
            RunStatusName(migration.RunStatus),
            migration.Checkpoint,
            migration.Counts,
            migration.AttentionReason);

    private static string StageName(MigrationStage stage) => stage switch
    {
        MigrationStage.Planned => "Nothing changed yet",
        MigrationStage.SupportingBothNames => "Old and new names both work",
        MigrationStage.CopyingInstances => "Copying existing instance data",
        MigrationStage.UpdatingHistory => "Updating property history",
        MigrationStage.CheckingResults => "Checking the migrated data",
        MigrationStage.UsingNewName => "Using the new name",
        MigrationStage.RemovingOldName => "Removing the old MongoDB field",
        MigrationStage.Finished => "Rename complete",
        _ => stage.ToString()
    };

    private static string RunStatusName(MigrationRunStatus status) => status switch
    {
        MigrationRunStatus.Waiting => "Waiting for approval",
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
    IReadOnlyList<MigrationPlanDto> ActivePlans,
    IReadOnlyList<MigrationPlanDto> PendingPlans,
    IReadOnlyList<MigrationDto> Migrations);