namespace UvA.Workflow.Persistence.Mongo;

public class MailLogRepository(IMongoDatabase database) : IMailLogRepository
{
    private readonly IMongoCollection<MailLogEntry> _mailLogCollection =
        database.GetCollection<MailLogEntry>("maillog");

    public async Task Log(MailLogEntry logEntry, CancellationToken ct = default)
    {
        await _mailLogCollection.InsertOneAsync(logEntry, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<MailLogEntry>> GetByInstance(string workflowInstanceId,
        CancellationToken ct = default)
    {
        return await _mailLogCollection
            .Find(x => x.WorkflowInstanceId == workflowInstanceId)
            .ToListAsync(ct);
    }
}