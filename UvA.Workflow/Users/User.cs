using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Users;

/// <summary>
/// Represents a user in the workflow system.
/// </summary>
[BsonIgnoreExtraElements]
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("UserName")] public string UserName { get; set; } = null!;

    [BsonElement("DisplayName")] public string DisplayName { get; set; } = null!;

    [BsonElement("Email")] public string Email { get; set; } = null!;

    [BsonElement("AuthProvider")]
    [JsonIgnore]
    public UserAuthProvider AuthProvider { get; set; } = UserAuthProvider.Internal;

    [BsonElement("IsActive")] [JsonIgnore] public bool IsActive { get; set; } = true;
}