using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Migrations;

public enum MigrationKind
{
    RenameProperty
}

public enum MigrationStatus
{
    Applying,
    Finished,
    Failed
}

/// <summary>
/// A durable record of an executed property migration.
/// </summary>
[BsonIgnoreExtraElements]
public class Migration
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string MigrationId { get; set; } = null!;

    /// <summary>Currently only <c>renameProperty</c> is supported.</summary>
    [BsonRepresentation(BsonType.String)]
    public MigrationKind Kind { get; set; }

    [BsonRepresentation(BsonType.String)] public MigrationStatus Status { get; set; }

    public string[] WorkflowDefinitions { get; set; } = [];
    public string OldProperty { get; set; } = null!;
    public string NewProperty { get; set; } = null!;

    public string RequestedBy { get; set; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime RequestedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? FinishedAt { get; set; }

    public long ItemsMatched { get; set; }
    public long ItemsUpdated { get; set; }
    public long JournalEntriesUpdated { get; set; }
    public string? Error { get; set; }
}