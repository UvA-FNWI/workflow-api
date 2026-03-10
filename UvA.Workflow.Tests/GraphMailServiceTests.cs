using Microsoft.Graph.Models;
using UvA.Workflow.Notifications;

namespace UvA.Workflow.Tests;

public class GraphMailServiceTests
{
    [Fact]
    public void BuildGraphMessage_WithOverrideRecipient_RewritesAllRecipients()
    {
        var mail = new MailMessage("Subject", "Body")
        {
            To = [new MailRecipient("to@uva.nl", "To User")],
            Cc = [new MailRecipient("cc@uva.nl", "Cc User")],
            Bcc = [new MailRecipient("bcc@uva.nl", "Bcc User")]
        };

        var graphMessage = GraphMailService.BuildGraphMessage(mail, "testen-dn-fnwi@uva.nl");

        Assert.Equal("testen-dn-fnwi@uva.nl", graphMessage.ToRecipients![0].EmailAddress?.Address);
        Assert.Equal("testen-dn-fnwi@uva.nl", graphMessage.CcRecipients![0].EmailAddress?.Address);
        Assert.Equal("testen-dn-fnwi@uva.nl", graphMessage.BccRecipients![0].EmailAddress?.Address);
    }

    [Fact]
    public void BuildGraphMessage_WithoutOverride_KeepsOriginalRecipients()
    {
        var mail = new MailMessage("Subject", "Body")
        {
            To = [new MailRecipient("to@uva.nl", "To User")],
            Cc = [new MailRecipient("cc@uva.nl", "Cc User")],
            Bcc = [new MailRecipient("bcc@uva.nl", "Bcc User")]
        };

        var graphMessage = GraphMailService.BuildGraphMessage(mail, null);

        Assert.Equal("to@uva.nl", graphMessage.ToRecipients![0].EmailAddress?.Address);
        Assert.Equal("cc@uva.nl", graphMessage.CcRecipients![0].EmailAddress?.Address);
        Assert.Equal("bcc@uva.nl", graphMessage.BccRecipients![0].EmailAddress?.Address);
    }

    [Fact]
    public void BuildGraphMessage_WithAttachments_MapsFileAttachments()
    {
        var attachmentBytes = new byte[] { 10, 20, 30 };
        var mail = new MailMessage("Subject", "Body")
        {
            To = [new MailRecipient("to@uva.nl")],
            Attachments = [new MailAttachment("file.txt", attachmentBytes)]
        };

        var graphMessage = GraphMailService.BuildGraphMessage(mail, null);

        Assert.Equal(BodyType.Html, graphMessage.Body!.ContentType);

        var graphAttachment = Assert.IsType<FileAttachment>(Assert.Single(graphMessage.Attachments!));
        Assert.Equal("file.txt", graphAttachment.Name);
        Assert.Equal("application/octet-stream", graphAttachment.ContentType);
        Assert.Equal(attachmentBytes, graphAttachment.ContentBytes);
    }
}