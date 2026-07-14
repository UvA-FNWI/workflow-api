using UvA.Workflow.Expressions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Notifications;

public class DummyMailService : IMailService
{
    public Task<MailDispatchResult> Send(MailMessage mail, CancellationToken ct = default)
    {
        Console.WriteLine(
            $"Sending mail to {mail.To.ToSeparatedString(t => t.MailAddress)}: {mail.Subject}\n{mail.Body}");
        return Task.FromResult(new MailDispatchResult(
            mail.To,
            mail.Cc ?? [],
            mail.Bcc ?? [],
            null));
    }
}

public record MailRecipient(string MailAddress, string? DisplayName = null)
{
    public static MailRecipient? FromUser(InstanceUser? user) =>
        user == null ? null : new MailRecipient(user.Email, user.DisplayName);
}

public record MailAttachment(string FileName, byte[] Content);

public record MailMessage(
    string Subject,
    string Body,
    string? AttachmentTemplate = null
)
{
    public List<MailRecipient> To { get; set; } = [];
    public List<MailRecipient>? Cc { get; set; }
    public List<MailRecipient>? Bcc { get; set; }
    public List<MailAttachment>? Attachments { get; set; }
}