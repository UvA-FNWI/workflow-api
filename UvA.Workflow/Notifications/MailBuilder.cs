using UvA.Workflow.Expressions;

namespace UvA.Workflow.Notifications;

public class MailBuilder(
    IWorkflowInstanceRepository repository,
    IMailLayout layout,
    IConfiguration configuration)
{
    public async Task<MailMessage> BuildAsync(
        WorkflowInstance instance,
        SendMessage sendMail,
        ModelService modelService,
        CancellationToken ct = default)
    {
        var context = modelService.CreateContext(instance);

        var recipient = sendMail.To != null
            ? MailRecipient.FromUser(context.Get(sendMail.To) as User)
            : new MailRecipient(sendMail.ToAddressTemplate!.Execute(context));
        recipient ??= new MailRecipient("invalid@invalid", "Invalid recipient");

        var (subject, bodyMarkdown) = sendMail.TemplateKey != null
            ? await ResolveTemplate(sendMail.TemplateKey, context, ct)
            : (sendMail.SubjectTemplate?.Execute(context) ?? "", sendMail.BodyTemplate?.Execute(context) ?? "");

        var htmlBody = MarkdownRenderer.ToHtml(bodyMarkdown);

        MailButton? button = null;
        if (sendMail.IncludeInstanceButton)
        {
            var baseUrl = configuration["FrontendBaseUrl"]?.TrimEnd('/');
            if (baseUrl is not null)
                button = new MailButton("Login", $"{baseUrl}/instances/{instance.Id}");
        }

        var fullHtml = layout.Render(htmlBody, button);

        return new MailMessage(subject, fullHtml) { To = [recipient] };
    }

    private async Task<(string Subject, string Body)> ResolveTemplate(
        string templateKey,
        ObjectContext context,
        CancellationToken ct)
    {
        var templates = await repository.GetAll(
            i => i.WorkflowDefinition == "Template",
            ct);

        var template =
            templates.FirstOrDefault(i => i.Properties.TryGetValue("Key", out var k) && k.AsString == templateKey);

        if (template is null)
            return ("", "");

        var subjectRaw = template.Properties.GetValueOrDefault("Subject")?.AsString ?? "";
        var bodyRaw = template.Properties.GetValueOrDefault("Content")?.AsString ?? "";

        var subject = Template.Create(subjectRaw)?.Execute(context) ?? "";
        var body = new Template(bodyRaw).Execute(context);

        return (subject, body);
    }
}