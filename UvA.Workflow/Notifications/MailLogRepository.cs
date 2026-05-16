namespace UvA.Workflow.Notifications;

public interface IMailLogRepository
{
    Task Log(MailLogEntry logEntry, CancellationToken ct = default);
}