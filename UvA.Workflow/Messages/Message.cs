using MongoDB.Bson.Serialization.Attributes;

namespace UvA.Workflow.Messages;

public class Message
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string InstanceId { get; set; } = null!;

    public string? QuestionName { get; set; }

    public MessageItem[] Items { get; set; } = [];

    public bool IsClosed => Items.LastOrDefault()?.Kind == ItemKind.Close;
}

public class MessageItem
{
    public DateTime DateTime { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string AuthorId { get; set; } = null!;

    public string? Body { get; set; }

    [BsonRepresentation(BsonType.String)] public ItemKind Kind { get; set; }
}

public enum ItemKind
{
    Message,
    Close
}