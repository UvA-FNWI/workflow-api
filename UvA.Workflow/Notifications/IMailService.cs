namespace UvA.Workflow.Notifications;

public interface IMailService
{
    Task<MailDispatchResult> Send(MailMessage mail, CancellationToken ct = default);
}

public record MailDispatchResult(
    IReadOnlyList<MailRecipient> To,
    IReadOnlyList<MailRecipient> Cc,
    IReadOnlyList<MailRecipient> Bcc,
    string? AppliedRecipientOverride = null);