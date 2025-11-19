using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.WorkflowInstances;

public class InstanceEventLogEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("InstanceId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string WorkflowInstanceId { get; set; } = null!;

    [BsonElement("EventId")] public string EventId { get; set; } = null!;

    [BsonElement("InitiatedBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string InitiatedBy { get; set; } = null!;

    [BsonElement("Date")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Date { get; set; }

    [BsonElement("Operation")] public string Operation { get; set; } = null!;
}