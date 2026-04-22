using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Notifications;

public class MailBuilder(
    IMailLayoutResolver layoutResolver,
    IConfiguration configuration)
{
    public Task<MailMessage> BuildAsync(
        WorkflowInstance instance,
        SendMessage sendMail,
        ModelService modelService,
        CancellationToken ct = default)
    {
        var resolvedMail =
            ResolveMessageContents(modelService.WorkflowDefinitions[instance.WorkflowDefinition], sendMail);
        var context = modelService.CreateContext(instance);
        var frontendBaseUrl = configuration["FrontendBaseUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(frontendBaseUrl))
            context.Values["FrontendBaseUrl"] = frontendBaseUrl;

        var recipient = resolvedMail.To != null
            ? MailRecipient.FromUser(context.Get(resolvedMail.To) as User)
            : new MailRecipient(resolvedMail.ToAddressTemplate!.Execute(context));
        recipient ??= new MailRecipient("invalid@invalid", "Invalid recipient");

        var subject = resolvedMail.SubjectTemplate?.Apply(context).En ?? "";
        var bodyMarkdown = resolvedMail.BodyTemplate?.Apply(context).En ?? "";

        var htmlBody = MarkdownRenderer.ToHtml(bodyMarkdown);

        var buttons = resolvedMail.Buttons
            .Select(b => new MailButton(
                b.LabelTemplate.Apply(context).En,
                b.UrlTemplate.Execute(context),
                b.Intent))
            .ToList();

        var layout = layoutResolver.Resolve(resolvedMail.Layout);
        var fullHtml = layout.Render(htmlBody, buttons);

        return Task.FromResult(new MailMessage(subject, fullHtml) { To = [recipient] });
    }

    private static SendMessage ResolveMessageContents(WorkflowDefinition workflowDefinition, SendMessage sendMail)
    {
        if (string.IsNullOrWhiteSpace(sendMail.TemplateKey))
            return sendMail;

        var template = workflowDefinition.Emails
            .FirstOrDefault(e => string.Equals(e.Name, sendMail.TemplateKey, StringComparison.OrdinalIgnoreCase));

        if (template == null)
        {
            var known = string.Join(", ", workflowDefinition.Emails.Select(m => m.Name).OrderBy(n => n));
            throw new InvalidOperationException(
                $"Mail template '{sendMail.TemplateKey}' not found in '{workflowDefinition.Name}'. Known templates: {known}");
        }

        return Merge(template, sendMail);
    }

    /// Inline values override template defaults.
    private static SendMessage Merge(SendMessage template, SendMessage inline)
    {
        return new SendMessage
        {
            Name = string.IsNullOrWhiteSpace(inline.Name) ? template.Name : inline.Name,
            TemplateKey = inline.TemplateKey ?? template.TemplateKey,

            To = inline.To ?? template.To,
            ToAddress = inline.ToAddress ?? template.ToAddress,
            Subject = inline.Subject ?? template.Subject,
            Body = inline.Body ?? template.Body,
            Layout = inline.Layout ?? template.Layout,

            Buttons = inline.Buttons is { Length: > 0 } ? inline.Buttons : template.Buttons,
            Attachments = inline.Attachments is { Length: > 0 } ? inline.Attachments : template.Attachments,

            SendAutomatically = inline.SendAutomatically,
            SendAsMail = inline.SendAsMail || template.SendAsMail
        };
    }
}