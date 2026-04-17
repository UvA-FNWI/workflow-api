namespace UvA.Workflow.Notifications;

public static class MailDeliveryResolver
{
    private static string ResolveAddress(MailRecipient recipient, string? overrideRecipient)
        => string.IsNullOrWhiteSpace(overrideRecipient) ? recipient.MailAddress : overrideRecipient;

    public static MailRecipient[] GetEffectiveRecipients(IEnumerable<MailRecipient>? recipients,
        string? overrideRecipient)
        => recipients?
               .Select(r => new MailRecipient(ResolveAddress(r, overrideRecipient), r.DisplayName))
               .ToArray()
           ?? [];
}