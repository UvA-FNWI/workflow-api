using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Journaling;

public class PropertyChangeEntry
{
    public DateTime Timestamp { get; private set; }
    public string Path { get; private set; } = null!;
    public BsonValue NewValue { get; private set; } = null!;
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
        BsonValue newValue,
        string modifiedBy)
    {
        Timestamp = timestamp;
        Path = path;
        NewValue = newValue;
        ModifiedBy = modifiedBy;
    }

    // Factory for your application code.
    public static PropertyChangeEntry Create(
        PropertyDefinition propertyDefinition,
        BsonValue newValue,
        User modifiedBy)
        => new(
            DateTime.Now,
            propertyDefinition.Name,
            newValue,
            modifiedBy.UserName);
}