using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Journaling;

public class InstanceJournalEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string InstanceId { get; set; } = null!;

    public int CurrentVersion { get; set; } = 0;

    public PropertyChangeEntry[] PropertyChanges { get; set; } = Array.Empty<PropertyChangeEntry>();
}