using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using Azure.Identity;

namespace UvA.Workflow.Notifications.Graph;

public class GraphMailService : IMailService
{
    private readonly GraphMailOptions _options;
    private readonly GraphServiceClient _graphClient;

    public GraphMailService(
        IOptions<GraphMailOptions> graphOptions,
        IGraphMailTokenStore tokenStore)
    {
        _options = graphOptions.Value;
        GraphMailOptions.Validate(_options);

        var credential = new ClientSecretCredential(
            _options.TenantId,
            _options.ClientId,
            _options.ClientSecret);

        _graphClient = new GraphServiceClient(credential);
    }

    public async Task<MailDispatchResult> Send(MailMessage mail, CancellationToken ct = default)
    {
        var dispatchResult = BuildDispatchResult(mail, _options.OverrideRecipient);
        var graphMessage = BuildGraphMessage(mail, dispatchResult);

        await _graphClient.Users[_options.SenderId]
            .SendMail
            .PostAsync(
                new SendMailPostRequestBody
                {
                    Message = graphMessage,
                    SaveToSentItems = true
                },
                cancellationToken: ct);

        return dispatchResult;
    }

    public static Message BuildGraphMessage(MailMessage mail, string? overrideRecipient)
        => BuildGraphMessage(mail, BuildDispatchResult(mail, overrideRecipient));

    internal static Message BuildGraphMessage(MailMessage mail, MailDispatchResult dispatchResult)
    {
        var graphMessage = new Message
        {
            Subject = mail.Subject,
            Body = new ItemBody
            {
                Content = mail.Body,
                ContentType = BodyType.Html
            },
            ToRecipients = BuildRecipients(dispatchResult.To),
            CcRecipients = BuildRecipients(dispatchResult.Cc),
            BccRecipients = BuildRecipients(dispatchResult.Bcc),
            Attachments = BuildAttachments(mail.Attachments)
        };
        return graphMessage;
    }

    internal static MailDispatchResult BuildDispatchResult(MailMessage mail, string? overrideRecipient)
        => new(
            MailDeliveryResolver.GetEffectiveRecipients(mail.To, overrideRecipient),
            MailDeliveryResolver.GetEffectiveRecipients(mail.Cc, overrideRecipient),
            MailDeliveryResolver.GetEffectiveRecipients(mail.Bcc, overrideRecipient),
            overrideRecipient);

    private static List<Recipient> BuildRecipients(IEnumerable<MailRecipient> recipients)
        => recipients
            .Select(ToRecipient)
            .ToList();

    private static List<Microsoft.Graph.Models.Attachment> BuildAttachments(IEnumerable<MailAttachment>? attachments)
        => attachments?
            .Select<MailAttachment, Microsoft.Graph.Models.Attachment>(attachment => new FileAttachment
            {
                OdataType = "#microsoft.graph.fileAttachment",
                Name = attachment.FileName,
                ContentType = "application/octet-stream",
                ContentBytes = attachment.Content
            })
            .ToList() ?? [];

    private static Recipient ToRecipient(MailRecipient recipient)
        => new()
        {
            EmailAddress = new EmailAddress
            {
                Address = recipient.MailAddress,
                Name = recipient.DisplayName
            }
        };
}