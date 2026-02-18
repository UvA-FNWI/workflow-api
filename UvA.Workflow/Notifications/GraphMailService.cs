using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;

namespace UvA.Workflow.Notifications;

internal class TokenProvider(Func<CancellationToken, Task<string>> getToken) : IAccessTokenProvider
{
    public AllowedHostsValidator AllowedHostsValidator { get; } = new(["graph.microsoft.com"]);

    public Task<string> GetAuthorizationTokenAsync(Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
        => getToken(cancellationToken);
}

public class GraphMailService : IMailService
{
    private static readonly string[] MailSendScopes = ["Mail.Send.Shared"];
    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(5);

    private readonly GraphMailOptions _options;
    private readonly GraphServiceClient _graphClient;
    private readonly IPublicClientApplication _publicClientApplication;
    private readonly IGraphMailTokenStore _tokenStore;

    private AuthenticationResult? _cachedAuthenticationResult;

    public GraphMailService(
        IOptions<GraphMailOptions> graphOptions,
        IGraphMailTokenStore tokenStore)
    {
        _options = graphOptions.Value;
        GraphMailOptions.Validate(_options);

        _tokenStore = tokenStore;

        var accessTokenProvider = new BaseBearerTokenAuthenticationProvider(new TokenProvider(GetToken));
        _graphClient = new GraphServiceClient(accessTokenProvider);

        _publicClientApplication = PublicClientApplicationBuilder
            .Create(_options.ClientId)
            .WithTenantId(_options.TenantId)
            .WithRedirectUri("http://localhost:8050")
            .Build();
    }

    private async Task<string> GetToken(CancellationToken ct = default)
    {
        if (_cachedAuthenticationResult?.ExpiresOn > DateTimeOffset.UtcNow.Add(TokenRefreshBuffer))
            return _cachedAuthenticationResult.AccessToken;

        var tokenCache = await _tokenStore.GetTokenCache(ct);

        _publicClientApplication.UserTokenCache.SetBeforeAccess(args =>
            args.TokenCache.DeserializeMsalV3(tokenCache));

        _cachedAuthenticationResult = await _publicClientApplication.AcquireTokenSilent(
            MailSendScopes,
            _options.UserAccount
        ).ExecuteAsync(ct);

        return _cachedAuthenticationResult.AccessToken;
    }

    public async Task Send(MailMessage mail)
    {
        var graphMessage = BuildGraphMessage(mail, _options.OverrideRecipient);

        await _graphClient.Users[MailSender.MilestonesGeneral.GetUserId()]
            .SendMail
            .PostAsync(
                new SendMailPostRequestBody
                {
                    Message = graphMessage,
                    SaveToSentItems = true
                });
    }

    internal static Message BuildGraphMessage(MailMessage mail, string? overrideRecipient)
    {
        var graphMessage = new Message
        {
            Subject = mail.Subject,
            Body = new ItemBody
            {
                Content = mail.Body,
                ContentType = BodyType.Html
            },
            ToRecipients = BuildRecipients(mail.To, overrideRecipient),
            CcRecipients = BuildRecipients(mail.Cc, overrideRecipient),
            BccRecipients = BuildRecipients(mail.Bcc, overrideRecipient),
            Attachments = BuildAttachments(mail.Attachments)
        };

        return graphMessage;
    }

    private static List<Recipient> BuildRecipients(IEnumerable<MailRecipient>? recipients, string? overrideRecipient)
        => MailDeliveryResolver.GetEffectiveRecipients(recipients, overrideRecipient)
            .Select(ToRecipient)
            .ToList();

    private static List<Microsoft.Graph.Models.Attachment>? BuildAttachments(IEnumerable<MailAttachment>? attachments)
        => attachments?
            .Select<MailAttachment, Microsoft.Graph.Models.Attachment>(attachment => new FileAttachment
            {
                OdataType = "#microsoft.graph.fileAttachment",
                Name = attachment.FileName,
                ContentType = "application/octet-stream",
                ContentBytes = attachment.Content
            })
            .ToList();

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