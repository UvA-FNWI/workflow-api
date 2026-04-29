using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Users;

public class InstanceUser
{
    /// <summary>
    /// The id of the underlying <see cref="User"/> document, when one exists.
    /// External users referenced in a workflow instance before the EduID
    /// invitation flow has run may not have an associated user row yet, in
    /// which case this is null and matching falls back to <see cref="Email"/>.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

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

    public static InstanceUser FromSearchResult(UserSearchResult result) => new()
    {
        UserName = result.UserName,
        DisplayName = result.DisplayName,
        Email = result.Email
    };
}