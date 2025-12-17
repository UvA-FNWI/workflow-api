using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Auditing;

public class PropertyValueChange
{
    public DateTime Timestamp { get; private set; }
    public string Path { get; private set; } = null!;
    public BsonValue? OldValue { get; private set; } = null!;
    public BsonValue NewValue { get; private set; } = null!;
    public string ModifiedBy { get; private set; } = null!;

    public int Version { get; set; } = 1;

    private PropertyValueChange()
    {
    }

    // MongoDB driver will use this for deserialization.
    [BsonConstructor]
    private PropertyValueChange(
        DateTime timestamp,
        string path,
        BsonValue? oldValue,
        BsonValue newValue,
        string modifiedBy)
    {
        Timestamp = timestamp;
        Path = path;
        OldValue = oldValue;
        NewValue = newValue;
        ModifiedBy = modifiedBy;
    }

    // Factory for your application code.
    public static PropertyValueChange Create(
        PropertyDefinition propertyDefinition,
        BsonValue? oldValue,
        BsonValue newValue,
        User modifiedBy)
        => new(
            DateTime.Now,
            propertyDefinition.Name,
            oldValue,
            newValue,
            modifiedBy.UserName);
}