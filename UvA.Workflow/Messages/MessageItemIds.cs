using MongoDB.Bson;

namespace UvA.Workflow.Messages;

public static class MessageItemIds
{
    public static string EnsureItemId(MessageItem item, int index) =>
        string.IsNullOrEmpty(item.Id) ? $"legacy-{index}" : item.Id;

    public static string CreateId() => ObjectId.GenerateNewId().ToString();
}