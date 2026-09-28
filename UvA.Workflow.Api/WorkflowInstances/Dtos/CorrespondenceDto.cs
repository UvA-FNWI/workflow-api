using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public enum CorrespondenceRecipientType
{
    To,
    Cc,
    Bcc
}

public record CorrespondenceRecipientDto(string? Name, string Email, CorrespondenceRecipientType Type)
{
    public static CorrespondenceRecipientDto From(MailLogRecipient recipient, CorrespondenceRecipientType type) =>
        new(recipient.DisplayName, recipient.MailAddress, type);
}

public record CorrespondenceDto(
    string Id,
    string Subject,
    DateTime Timestamp,
    CorrespondenceRecipientDto[] Recipients,
    string Body,
    string[] Attachments)
{
    private static CorrespondenceRecipientDto[] GetRecipients(MailLogEntry entry) =>
        entry.To.Select(r => CorrespondenceRecipientDto.From(r, CorrespondenceRecipientType.To))
            .Concat(entry.Cc.Select(r => CorrespondenceRecipientDto.From(r, CorrespondenceRecipientType.Cc)))
            .Concat(entry.Bcc.Select(r => CorrespondenceRecipientDto.From(r, CorrespondenceRecipientType.Bcc)))
            .ToArray();

    public static CorrespondenceDto From(MailLogEntry entry) => new(
        entry.Id,
        entry.Subject,
        entry.Timestamp,
        GetRecipients(entry),
        entry.Body,
        entry.Attachments.Select(a => a.Name).ToArray()
    );
}