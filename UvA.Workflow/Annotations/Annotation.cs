using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Annotations;

public class Annotation
{
    [BsonId] public ObjectId Id { get; set; }

    public required string InstanceId { get; set; }
    public required string SubmissionId { get; set; }
    public required string ArtifactId { get; set; }

    /// <summary>
    /// The selected text that was highlighted by the reviewer.
    /// </summary>
    public required string HighlightedText { get; set; }

    /// <summary>
    /// The reviewer's comment on the highlighted section.
    /// </summary>
    public required string Comment { get; set; }

    /// <summary>
    /// Serialised position from react-pdf-highlighter (boundingRect, rects[], pageNumber).
    /// Stored as a raw BsonDocument so the shape can evolve without migrations.
    /// </summary>
    public required BsonDocument Position { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}