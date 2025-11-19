using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Users;

/// <summary>
/// Represents a user in the workflow system.
/// </summary>
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    //TODO: Consider using a different field name for external ID
    [BsonElement("ExternalId")] public string UserName { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Email { get; set; } = null!;

    /// <summary>
    /// Creates a MailRecipient representation from this User.
    /// </summary>
    public MailRecipient ToRecipient() => new(Email, DisplayName);

    /// <summary>
    /// Creates an ExternalUser representation from this User.
    /// </summary>
    public ExternalUser ToExternalUser() => new(UserName, DisplayName, Email);
}

/// <summary>
/// Represents an external user reference.
/// </summary>
public record ExternalUser(string UserName, string DisplayName, string Email);