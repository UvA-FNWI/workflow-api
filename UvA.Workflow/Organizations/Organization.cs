using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Organizations;

/// <summary>
/// Represents an organization in the workflow system.
/// </summary>
[BsonIgnoreExtraElements]
public class Organization
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("Name")] public string Name { get; set; } = null!;


    public static Organization Create(string name) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        Name = name
    };
}