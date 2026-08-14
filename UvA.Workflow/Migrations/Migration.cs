using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Migrations;

public enum MigrationKind
{
    RenameProperty
}

public enum MigrationStatus
{
    Applying,
    ReadyToFinish,
    Finishing,
    Finished,
    Reverting,
    Reverted,
    ApplyFailed,
    FinishFailed,
    RevertFailed
}

[BsonIgnoreExtraElements]
public class Migration
{
    [BsonId] public string Id { get; set; } = null!;

    [BsonRepresentation(BsonType.String)] public MigrationKind Kind { get; set; }
    [BsonRepresentation(BsonType.String)] public MigrationStatus Status { get; set; }

    public string Name { get; set; } = null!;
    public MigrationDefinition Definition { get; set; } = null!;
    public string? Description { get; set; }
    public string RequestedBy { get; set; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime RequestedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? FinishedAt { get; set; }

    public MigrationProgress Progress { get; set; } = new();
    public string? Error { get; set; }
}

[BsonDiscriminator(RootClass = true)]
[BsonKnownTypes(typeof(RenamePropertyDefinition))]
public abstract class MigrationDefinition
{
}

public class RenamePropertyDefinition : MigrationDefinition
{
    public string WorkflowDefinition { get; set; } = null!;
    public string OldProperty { get; set; } = null!;
    public string NewProperty { get; set; } = null!;
}

public class MigrationProgress
{
    public long ItemsMatched { get; set; }
    public long ItemsUpdated { get; set; }
    public Dictionary<string, long> Details { get; set; } = [];
}