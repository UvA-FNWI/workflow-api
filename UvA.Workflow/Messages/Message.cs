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

    public bool IsClosed
    {
        get
        {
            for (var i = Items.Length - 1; i >= 0; i--)
            {
                switch (Items[i].Kind)
                {
                    case ItemKind.Close:
                        return true;
                    case ItemKind.Reopen:
                        return false;
                }
            }

            return false;
        }
    }
}

public class MessageItem
{
    /// <summary>Stable id for nested replies; stored as string (includes legacy-{index} for older items).</summary>
    public string Id { get; set; } = null!;

    public string? ParentItemId { get; set; }

    public DateTime DateTime { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string AuthorId { get; set; } = null!;

    public string? Body { get; set; }

    [BsonRepresentation(BsonType.String)] public ItemKind Kind { get; set; }
}

public enum ItemKind
{
    Message,
    Close,
    Reopen
}