namespace UvA.Workflow.Messages;

public interface IMessageRepository
{
    Task<IReadOnlyList<Message>> GetMessages(string instanceId, CancellationToken ct);
    Task AddMessage(Message message, CancellationToken ct);
    Task<Message?> AddMessageItem(string instanceId, string messageId, MessageItem item, CancellationToken ct);
}