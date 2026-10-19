namespace UvA.Workflow.Notifications;

public interface IMailLogRepository
{
    Task Log(MailLogEntry logEntry, CancellationToken ct = default);
    Task<IReadOnlyList<MailLogEntry>> GetByInstance(string workflowInstanceId, CancellationToken ct = default);
}