using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Auditing;

public class WorkflowInstanceChangeSet
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string InstanceId { get; set; } = null!;

    public int CurrentVersion { get; set; } = 0;

    public PropertyValueChange[] PropertyChanges { get; set; } = Array.Empty<PropertyValueChange>();
}