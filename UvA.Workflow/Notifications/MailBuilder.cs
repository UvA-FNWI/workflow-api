using UvA.Workflow.Expressions;

namespace UvA.Workflow.Notifications;

public class MailBuilder(
    IMailLayoutResolver layoutResolver,
    IConfiguration configuration)
{
    public async Task<MailMessage> BuildAsync(
        WorkflowInstance instance,
        SendMessage sendMail,
        ModelService modelService,
        CancellationToken ct = default)
    {
        var context = modelService.CreateContext(instance);
        var frontendBaseUrl = configuration["FrontendBaseUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(frontendBaseUrl))
            context.Values["FrontendBaseUrl"] = frontendBaseUrl;

        var recipientUser = sendMail.To != null ? context.Get(sendMail.To) as InstanceUser : null;
        var recipient = sendMail.To != null
            ? MailRecipient.FromUser(recipientUser)
            : new MailRecipient(sendMail.ToAddressTemplate!.Execute(context));
        recipient ??= new MailRecipient("invalid@invalid", "Invalid recipient");
        var language = recipientUser?.PreferredLanguage;

        var subject = sendMail.SubjectTemplate?.Apply(context).ForLanguage(language) ?? "";
        var bodyMarkdown = sendMail.BodyTemplate?.Apply(context).ForLanguage(language) ?? "";

        var htmlBody = MarkdownRenderer.ToHtml(bodyMarkdown);

        var buttons = sendMail.Buttons
            .Select(b => new MailButton(
                b.LabelTemplate.Apply(context).ForLanguage(language),
                b.UrlTemplate.Execute(context),
                b.Intent))
            .ToList();

        var layout = layoutResolver.Resolve(sendMail.Layout);
        var fullHtml = layout.Render(htmlBody, buttons);

        return new MailMessage(subject, fullHtml) { To = [recipient] };
    }
}