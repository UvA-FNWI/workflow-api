namespace UvA.Workflow.Notifications;

public class GraphMailOptions
{
    public const string TokenSettingKey = "GraphMailToken";
    public const string Section = "GraphMail";

    public string TenantId { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    public string UserAccount { get; set; } = null!;
    public string? OverrideRecipient { get; set; }

    public static void Validate(GraphMailOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TenantId))
            throw new InvalidOperationException("Missing GraphMail.TenantId");
        if (string.IsNullOrWhiteSpace(options.ClientId))
            throw new InvalidOperationException("Missing GraphMail.ClientId");
        if (string.IsNullOrWhiteSpace(options.UserAccount))
            throw new InvalidOperationException("Missing GraphMail.UserAccount");
    }
}