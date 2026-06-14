using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Users;

/// <summary>
/// A state to represent a possible invitation for an (external) user.
/// </summary>
public enum UserInvitationState
{
    ///<summary>When a user still 'requires' an invitation.</summary>
    Required,

    ///<summary>The user already has a 'pending' invitation.</summary>
    Pending,

    ///<summary>The user has already been invited and 'completed' this by logging in.</summary>
    Completed
}

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

    [BsonElement("PreferredLanguage")]
    [BsonIgnoreIfNull]
    public string? PreferredLanguage { get; set; }

    [BsonElement("Organization")] public Organization? Organization { get; set; }

    [BsonElement("ProviderKey")]
    [JsonIgnore]
    public string ProviderKey { get; set; } = UserProviderKeys.Internal;

    [BsonElement("IsActive")] [JsonIgnore] public bool IsActive { get; set; } = true;

    [BsonElement("InvitationState")]
    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public UserInvitationState? InvitationState { get; set; } = null;
}