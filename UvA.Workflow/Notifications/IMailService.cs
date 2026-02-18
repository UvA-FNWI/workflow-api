namespace UvA.Workflow.Notifications;

public interface IMailService
{
    Task Send(MailMessage mail);
}

public enum MailSender
{
    MilestonesGeneral,
}

public static class MailSenderExtensions
{
    extension(MailSender sender)
    {
        public string GetUserId() => sender switch
        {
            MailSender.MilestonesGeneral =>
                "0c6948d0-009a-4796-9d94-e0b56bc3ea9c", // TODO: Replace with milestones mail
            _ => throw new ArgumentOutOfRangeException(nameof(sender), sender, null)
        };
    }
}