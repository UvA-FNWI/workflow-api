using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Users;

public class InstanceUser
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("UserName")] public string UserName { get; set; } = null!;

    [BsonElement("DisplayName")] public string DisplayName { get; set; } = null!;

    [BsonElement("Email")] public string Email { get; set; } = null!;

    public static InstanceUser FromUser(User user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        DisplayName = user.DisplayName,
        Email = user.Email
    };
}