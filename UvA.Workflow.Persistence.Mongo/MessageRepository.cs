namespace UvA.Workflow.Persistence.Mongo;

public class MessageRepository(IMongoDatabase database) : IMessageRepository
{
    private readonly IMongoCollection<Message> _messages =
        database.GetCollection<Message>("messages");

    public async Task<IReadOnlyList<Message>> GetMessages(string instanceId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(instanceId, out _))
            return [];

        var cursor = await _messages.FindAsync(
            Builders<Message>.Filter.Eq(m => m.InstanceId, instanceId),
            cancellationToken: ct);
        return await cursor.ToListAsync(ct);
    }

    public Task AddMessage(Message message, CancellationToken ct)
        => _messages.InsertOneAsync(message, cancellationToken: ct);

    public async Task<Message?> AddMessageItem(string instanceId, string messageId, MessageItem item,
        CancellationToken ct)
    {
        if (!ObjectId.TryParse(instanceId, out _) || !ObjectId.TryParse(messageId, out _))
            return null;

        return await _messages.FindOneAndUpdateAsync(
            Builders<Message>.Filter.And(
                Builders<Message>.Filter.Eq(m => m.InstanceId, instanceId),
                Builders<Message>.Filter.Eq(m => m.Id, messageId)),
            Builders<Message>.Update.Push(m => m.Items, item),
            new FindOneAndUpdateOptions<Message> { ReturnDocument = ReturnDocument.After },
            ct);
    }
}