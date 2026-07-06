using Microsoft.Extensions.Hosting;
using UvA.Workflow.Expressions;
using UvA.Workflow.Jobs;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Notifications;

public class MailBuilder(
    IMailLayoutResolver layoutResolver,
    IConfiguration configuration,
    IOptions<WorkerOptions> workerOptions,
    IHostEnvironment environment)
{
    public async Task<MailMessage> BuildAsync(
        WorkflowInstance instance,
        SendMessage sendMail,
        ModelService modelService,
        CancellationToken ct = default,
        ObjectContext? context = null)
    {
        var resolvedMail =
            ResolveMessageContents(modelService.WorkflowDefinitions[instance.WorkflowDefinition], sendMail);
        context ??= modelService.CreateContext(instance);
        var frontendBaseUrl = configuration["FrontendBaseUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(frontendBaseUrl))
            context.Values["FrontendBaseUrl"] = frontendBaseUrl;

        var to = ResolveRecipients(resolvedMail.To, context);
        if (resolvedMail.ToAddressTemplate != null)
            to.Add(new MailRecipient(resolvedMail.ToAddressTemplate.Execute(context)));
        to = Deduplicate(to);

        var cc = Deduplicate(ResolveRecipients(resolvedMail.Cc, context));
        var bcc = Deduplicate(ResolveRecipients(resolvedMail.Bcc, context));

        // Only flag a misconfigured mail when it has no recipients at all; Cc/Bcc-only mails are valid
        if (to.Count == 0 && cc.Count == 0 && bcc.Count == 0)
            to = [new MailRecipient("invalid@invalid", "Invalid recipient")];

        // Send in Dutch only when every To user prefers it; otherwise English, which everyone reads.
        var toUsers = ResolveUsers(resolvedMail.To, context).ToList();
        var language = toUsers.Count > 0 && toUsers.All(u => IsDutch(u.PreferredLanguage)) ? "nl" : "en";

        var subject = resolvedMail.SubjectTemplate?.Apply(context).ForLanguage(language) ?? "";

        if (!environment.IsProduction())
            subject = $"[{workerOptions.Value.WorkerGroup}] {subject}";

        var bodyMarkdown = resolvedMail.BodyTemplate?.Apply(context).ForLanguage(language) ?? "";

        var htmlBody = MarkdownRenderer.ToHtml(bodyMarkdown);

        var buttons = resolvedMail.Buttons
            .Select(b => new MailButton(
                b.LabelTemplate.Apply(context).ForLanguage(language),
                b.UrlTemplate.Execute(context),
                b.Intent))
            .ToList();

        var layout = layoutResolver.Resolve(resolvedMail.Layout);
        var fullHtml = layout.Render(htmlBody, buttons);

        return new MailMessage(subject, fullHtml)
        {
            To = to,
            Cc = cc.Count > 0 ? cc : null,
            Bcc = bcc.Count > 0 ? bcc : null
        };
    }

    /// Resolves a recipient list to mail recipients, expanding [User] arrays and literal addresses.
    private static List<MailRecipient> ResolveRecipients(Recipients? specs, ObjectContext context)
        => ResolveUsers(specs, context).Select(u => MailRecipient.FromUser(u)!)
            .Concat(ResolveAddresses(specs, context))
            .ToList();

    private static IEnumerable<InstanceUser> ResolveUsers(Recipients? specs, ObjectContext context)
        => (specs ?? [])
            .Where(s => !Recipients.IsAddress(s))
            .SelectMany(s => context.Get(s) switch
            {
                InstanceUser user => [user],
                IEnumerable<InstanceUser> users => users,
                _ => []
            });

    private static IEnumerable<MailRecipient> ResolveAddresses(Recipients? specs, ObjectContext context)
        => (specs ?? [])
            .Where(Recipients.IsAddress)
            .Select(s => new MailRecipient(Template.Create(s)!.Execute(context)));

    private static List<MailRecipient> Deduplicate(IEnumerable<MailRecipient> recipients)
        => recipients.DistinctBy(r => r.MailAddress, StringComparer.OrdinalIgnoreCase).ToList();

    // Mirrors BilingualString.ForLanguage's "nl" prefix rule.
    private static bool IsDutch(string? language)
        => language?.StartsWith("nl", StringComparison.OrdinalIgnoreCase) == true;

    /// Recipient lookups after applying template defaults, so callers can enrich referenced properties.
    public static IEnumerable<Lookup> ResolveRecipientLookups(WorkflowDefinition workflowDefinition,
        SendMessage sendMail)
        => ResolveMessageContents(workflowDefinition, sendMail).RecipientLookups;

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
            TemplateKey = inline.TemplateKey ?? template.TemplateKey,

            To = inline.To ?? template.To,
            ToAddress = inline.ToAddress ?? template.ToAddress,
            Cc = inline.Cc ?? template.Cc,
            Bcc = inline.Bcc ?? template.Bcc,
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