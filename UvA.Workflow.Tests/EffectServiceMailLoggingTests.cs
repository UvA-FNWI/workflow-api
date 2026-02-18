using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Events;
using UvA.Workflow.Notifications;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class EffectServiceMailLoggingTests
{
    [Fact]
    public async Task RunEffects_WithMailEffect_SendsMailAndLogsFullContent()
    {
        var modelService = new ModelService(new ModelParser(new FileSystemProvider("../../../../Examples/Projects")));
        var instanceRepository = new Mock<IWorkflowInstanceRepository>();
        var userService = new Mock<IUserService>();
        var rightsService = new RightsService(modelService, userService.Object, instanceRepository.Object);
        var instanceService =
            new InstanceService(instanceRepository.Object, modelService, userService.Object, rightsService);

        var eventService = new Mock<IInstanceEventService>();
        var mailService = new Mock<IMailService>();
        var mailLogRepository = new Mock<IMailLogRepository>();

        var effectService = new EffectService(
            instanceService,
            eventService.Object,
            modelService,
            mailService.Object,
            mailLogRepository.Object,
            Options.Create(new GraphMailOptions
            {
                TenantId = "tenant",
                ClientId = "client",
                UserAccount = "user@mail.com",
                OverrideRecipient = "testen-dn-fnwi@uva.nl"
            }));

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .Build();
        var user = new User { Id = "507f1f77bcf86cd799439011" };

        byte[] attachmentBytes = [1, 2, 3, 4];
        var mail = new MailMessage("Subject", "Body", "attachment-template")
        {
            To = [new MailRecipient("to@uva.nl", "To User")],
            Cc = [new MailRecipient("cc@uva.nl", "Cc User")],
            Bcc = [new MailRecipient("bcc@uva.nl", "Bcc User")],
            Attachments = [new MailAttachment("test.txt", attachmentBytes)]
        };

        MailLogEntry? loggedEntry = null;
        mailLogRepository
            .Setup(r => r.Log(It.IsAny<MailLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MailLogEntry, CancellationToken>((entry, _) => loggedEntry = entry)
            .Returns(Task.CompletedTask);

        var effect = new Effect
        {
            SendMail = new SendMessage()
        };

        await effectService.RunEffects(instance, [effect], user, CancellationToken.None, mail);

        mailService.Verify(m => m.Send(It.IsAny<MailMessage>()), Times.Once);
        mailLogRepository.Verify(r => r.Log(It.IsAny<MailLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.NotNull(loggedEntry);
        Assert.Equal(instance.Id, loggedEntry!.WorkflowInstanceId);
        Assert.Equal("Project", loggedEntry.WorkflowDefinition);
        Assert.Equal(user.Id, loggedEntry.ExecutedBy);
        Assert.Equal("Subject", loggedEntry.Subject);
        Assert.Equal("Body", loggedEntry.Body);
        Assert.Equal("attachment-template", loggedEntry.AttachmentTemplate);

        Assert.Single(loggedEntry.To);
        Assert.Equal("testen-dn-fnwi@uva.nl", loggedEntry.To[0].MailAddress);
        Assert.Single(loggedEntry.Cc);
        Assert.Equal("testen-dn-fnwi@uva.nl", loggedEntry.Cc[0].MailAddress);
        Assert.Single(loggedEntry.Bcc);
        Assert.Equal("testen-dn-fnwi@uva.nl", loggedEntry.Bcc[0].MailAddress);

        Assert.Single(loggedEntry.Attachments);
        Assert.Equal("test.txt", loggedEntry.Attachments[0].FileName);
        Assert.Equal(attachmentBytes, loggedEntry.Attachments[0].Content);
    }
}