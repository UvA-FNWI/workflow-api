namespace UvA.Workflow.Notifications;

public interface IMailLogRepository
{
    Task Log(MailLogEntry logEntry, CancellationToken ct = default);
}

public class MailLogRepository(IMongoDatabase database) : IMailLogRepository
{
    private readonly IMongoCollection<MailLogEntry> _mailLogCollection =
        database.GetCollection<MailLogEntry>("maillog");

    public async Task Log(MailLogEntry logEntry, CancellationToken ct = default)
    {
        await _mailLogCollection.InsertOneAsync(logEntry, cancellationToken: ct);
    }
}