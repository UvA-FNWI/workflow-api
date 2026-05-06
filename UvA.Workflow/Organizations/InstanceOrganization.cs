using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Organizations;

public class InstanceOrganization
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("Name")] public string Name { get; set; } = null!;

    public static InstanceOrganization FromOrganization(Organization organization) => new()
    {
        Id = organization.Id,
        Name = organization.Name
    };
}