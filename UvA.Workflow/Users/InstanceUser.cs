using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Users;

[BsonIgnoreExtraElements]
public class InstanceUser
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("UserName")] public string UserName { get; set; } = null!;

    [BsonElement("DisplayName")] public string DisplayName { get; set; } = null!;

    [BsonElement("Email")] public string Email { get; set; } = null!;

    [BsonElement("PreferredLanguage")]
    [BsonIgnoreIfNull]
    public string? PreferredLanguage { get; set; }

    [BsonElement("Organization")] public Organization? Organization { get; set; }

    [BsonElement("IsExternal")] public bool IsExternal { get; set; }

    [BsonElement("InvitationState")]
    [BsonIgnoreIfNull]
    public UserInvitationState? InvitationState { get; set; } = null;

    public static InstanceUser FromUser(User user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        DisplayName = user.DisplayName,
        Email = user.Email,
        PreferredLanguage = user.PreferredLanguage,
        Organization = user.Organization,
        IsExternal = UserProviderKeys.IsExternal(user.ProviderKey),
        InvitationState = user.InvitationState
    };
}