using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.WorkflowInstances;

public class InstanceEventLogEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("Timestamp")] public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [BsonElement("InstanceId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string WorkflowInstanceId { get; set; } = null!;

    [BsonElement("EventId")] public string EventId { get; set; } = null!;

    [BsonElement("ExecutedBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ExecutedBy { get; set; } = null!;

    [BsonElement("EventDate")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? EventDate { get; set; }

    [BsonElement("Operation")] public string Operation { get; set; } = null!;
}