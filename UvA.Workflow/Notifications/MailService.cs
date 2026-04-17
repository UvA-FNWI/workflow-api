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

public enum MailSender
{
    MilestonesGeneral,
}

public static class MailSenderExtensions
{
    public static string GetUserId(this MailSender sender) => sender switch
    {
        MailSender.MilestonesGeneral =>
            "0c6948d0-009a-4796-9d94-e0b56bc3ea9c", // TODO: Replace with milestones mail
        _ => throw new ArgumentOutOfRangeException(nameof(sender), sender, null)
    };
}

public record Mail(MailRecipient[] To, string Subject, string Body, string? AttachmentTemplate)
{
    public static async Task<Mail?> FromModel(WorkflowInstance inst, SendMessage? mail, ModelService modelService)
    {
        if (mail == null || (mail.To == null && mail.ToAddressTemplate == null))
            return null;
        var context = modelService.CreateContext(inst);
        var recipient = mail.To != null
            ? MailRecipient.FromUser(context.Get(mail.To) as InstanceUser)
            : new MailRecipient(mail.ToAddressTemplate!.Execute(context));
        recipient ??= new MailRecipient("invalid@invalid", "Invalid recipient");
        var attachment = mail.Attachments.FirstOrDefault();

        var (subject, body) = mail.TemplateKey != null
            ? await GenerateTemplate(mail.TemplateKey, inst, modelService, context)
            : (mail.SubjectTemplate?.Apply(context).En, mail.BodyTemplate!.Apply(context).En);
        var (_, attachmentContent) = attachment != null
            ? await GenerateTemplate(attachment.Template, inst, modelService, context)
            : (null, null);

        return new Mail([recipient], subject ?? "", body, attachmentContent);
    }

    public MailMessage ToMailMessage() => new(Subject, Body, AttachmentTemplate)
    {
        To = To.ToList()
    };

    private static Task<(string? Subject, string Content)> GenerateTemplate(string templateKey,
        WorkflowInstance instance, ModelService modelService, ObjectContext context)
    {
        var form = modelService.WorkflowDefinitions["Template"].Forms.Single();

        // var submission = await contextService.DataClient.Get(
        //     t => t.WorkflowDefinition == "Template" && t.FormSubmissions[form.Name].Answers["Key"] == templateKey,
        //     t => t.FormSubmissions[form.Name]
        // );

        var contentText = ""; // submission.Answers["Content"].AsString;
        var subjectText = ""; // submission.Answers["Subject"].AsString;
        var contentTemplate = new Template(contentText);
        var subjectTemplate = Template.Create(subjectText);
        var content = contentTemplate.Execute(context);
        var subject = subjectTemplate?.Execute(context);

        return Task.FromResult<(string?, string)>((subject, content));
    }
}

public record MailMessage(
    string Subject,
    string Body,
    string? AttachmentTemplate = null
)
{
    public MailSender? Sender { get; set; }
    public List<MailRecipient> To { get; set; } = [];
    public List<MailRecipient>? Cc { get; set; }
    public List<MailRecipient>? Bcc { get; set; }
    public List<MailAttachment>? Attachments { get; set; }
}