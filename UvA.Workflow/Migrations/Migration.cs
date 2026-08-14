using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Migrations;

public enum MigrationStage
{
    Planned,
    SupportingBothNames,
    CopyingInstances,
    UpdatingHistory,
    CheckingResults,
    UsingNewName,
    RemovingOldName,
    Finished
}

public enum MigrationRunStatus
{
    Waiting,
    Running,
    Paused,
    NeedsAttention,
    Done
}

/// <summary>Durable operational state copied from an activated YAML migration plan.</summary>
[BsonIgnoreExtraElements]
public class Migration
{
    [BsonId] public string Id { get; set; } = null!;

    [BsonRepresentation(BsonType.String)] public MigrationKind Kind { get; set; }

    public string WorkflowDefinition { get; set; } = null!;
    public string OldPath { get; set; } = null!;
    public string NewPath { get; set; } = null!;
    public string? Description { get; set; }
    public string DefinitionChecksum { get; set; } = null!;
    public string SourceCommit { get; set; } = null!;
    public string TargetCommit { get; set; } = null!;

    [BsonRepresentation(BsonType.String)] public MigrationStage Stage { get; set; } = MigrationStage.Planned;

    [BsonRepresentation(BsonType.String)]
    public MigrationRunStatus RunStatus { get; set; } = MigrationRunStatus.Waiting;

    public string RequestedBy { get; set; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime RequestedAt { get; set; }

    public MigrationCheckpoint Checkpoint { get; set; } = new();
    public MigrationCounts Counts { get; set; } = new();
    public string? AttentionReason { get; set; }
}

public class MigrationCheckpoint
{
    public string? LastInstanceId { get; set; }
    public string? LastJournalInstanceId { get; set; }
}

public class MigrationCounts
{
    public long InstancesFound { get; set; }
    public long InstancesCopied { get; set; }
    public long JournalsUpdated { get; set; }
    public long Conflicts { get; set; }
}