using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Organisations;

public class InstanceOrganisation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("Name")] public string Name { get; set; } = null!;

    public static InstanceOrganisation FromOrganisation(Organisation organisation) => new()
    {
        Id = organisation.Id,
        Name = organisation.Name
    };
}