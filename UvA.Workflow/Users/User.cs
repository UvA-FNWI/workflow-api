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

    public string ExternalId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Email { get; set; } = null!;

    /// <summary>
    /// Creates a MailRecipient representation from this User.
    /// </summary>
    public MailRecipient ToRecipient() => new(Email, DisplayName);

    /// <summary>
    /// Creates an ExternalUser representation from this User.
    /// </summary>
    public ExternalUser ToExternalUser() => new(ExternalId, DisplayName, Email);
}

/// <summary>
/// Represents an external user reference.
/// </summary>
public record ExternalUser(string Id, string DisplayName, string Email);