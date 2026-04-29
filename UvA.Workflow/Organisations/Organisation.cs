using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Organisations;

/// <summary>
/// Represents an organisation in the workflow system.
/// </summary>
public class Organisation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("Name")] public string Name { get; set; } = null!;

    // Included for parity with the User model; currently not exposed via the public API.
    [BsonElement("IsActive")] [JsonIgnore] public bool IsActive { get; set; } = true;
}