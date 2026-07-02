namespace UvA.Workflow.Notifications.Graph;

public class GraphMailOptions
{
    public const string Section = "GraphMail";

    public string TenantId { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    public string ClientSecret { get; set; } = null!;
    public string SenderId { get; set; } = null!;
    public string? OverrideRecipient { get; set; }

    public static void Validate(GraphMailOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TenantId))
            throw new InvalidOperationException("Missing GraphMail.TenantId");
        if (string.IsNullOrWhiteSpace(options.ClientId))
            throw new InvalidOperationException("Missing GraphMail.ClientId");
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            throw new InvalidOperationException("Missing GraphMail.ClientSecret");
        if (string.IsNullOrWhiteSpace(options.SenderId))
            throw new InvalidOperationException("Missing GraphMail.SenderId");
    }
}