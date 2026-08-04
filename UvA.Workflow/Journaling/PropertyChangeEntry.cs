using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Journaling;

public class PropertyChangeEntry
{
    public DateTime Timestamp { get; private set; }
    public string Path { get; private set; } = null!;
    public BsonValue? OldValue { get; private set; }
    public string ModifiedBy { get; private set; } = null!;

    public int Version { get; set; } = 1;

    private PropertyChangeEntry()
    {
    }

    // MongoDB driver will use this for deserialization.
    [BsonConstructor]
    private PropertyChangeEntry(
        DateTime timestamp,
        string path,
        BsonValue? oldValue,
        string modifiedBy)
    {
        Timestamp = timestamp;
        Path = path;
        OldValue = oldValue;
        ModifiedBy = modifiedBy;
    }

    // Journal replay splits dotted paths to restore nested values.
    public static PropertyChangeEntry Create(
        string path,
        BsonValue? oldValue,
        User modifiedBy)
        => new(
            DateTime.Now,
            path,
            oldValue,
            modifiedBy.UserName);
}