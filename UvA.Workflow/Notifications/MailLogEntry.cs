using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Notifications;

public class MailLogEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("Timestamp")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [BsonElement("InstanceId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string WorkflowInstanceId { get; set; } = null!;

    [BsonElement("WorkflowDefinition")] public string WorkflowDefinition { get; set; } = null!;

    [BsonElement("ExecutedBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string ExecutedBy { get; set; } = null!;

    [BsonElement("OverrideRecipient")] public string? OverrideRecipient { get; set; }
    [BsonElement("Subject")] public string Subject { get; set; } = null!;
    [BsonElement("Body")] public string Body { get; set; } = null!;
    [BsonElement("AttachmentTemplate")] public string? AttachmentTemplate { get; set; }
    [BsonElement("To")] public MailLogRecipient[] To { get; set; } = [];
    [BsonElement("Cc")] public MailLogRecipient[] Cc { get; set; } = [];
    [BsonElement("Bcc")] public MailLogRecipient[] Bcc { get; set; } = [];
    [BsonElement("Attachments")] public MailLogAttachment[] Attachments { get; set; } = [];
}

public record MailLogRecipient(string MailAddress, string? DisplayName = null);

public record MailLogAttachment(string FileName, byte[] Content);