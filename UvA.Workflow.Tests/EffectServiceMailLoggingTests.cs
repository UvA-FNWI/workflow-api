using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Events;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
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
        var artifactService = new Mock<IArtifactService>();
        var mailLogRepository = new Mock<IMailLogRepository>();

        var effectService = new EffectService(
            instanceService,
            eventService.Object,
            modelService,
            mailService.Object,
            artifactService.Object,
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

        artifactService
            .Setup(a => a.SaveArtifact(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync((string name, byte[] _) => new ArtifactInfo(MongoDB.Bson.ObjectId.GenerateNewId(), name));

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
        Assert.Equal("Start", loggedEntry.StepName);
        Assert.Null(loggedEntry.TriggerType);
        Assert.Null(loggedEntry.ActionName);
        Assert.Null(loggedEntry.FormId);
        Assert.Null(loggedEntry.TemplateKey);
        Assert.Equal("Subject", loggedEntry.Subject);
        Assert.Equal("Body", loggedEntry.Body);
        Assert.Equal("attachment-template", loggedEntry.AttachmentTemplate);

        Assert.Single(loggedEntry.To);
        Assert.Equal("to@uva.nl", loggedEntry.To[0].MailAddress);
        Assert.Single(loggedEntry.Cc);
        Assert.Equal("cc@uva.nl", loggedEntry.Cc[0].MailAddress);
        Assert.Single(loggedEntry.Bcc);
        Assert.Equal("bcc@uva.nl", loggedEntry.Bcc[0].MailAddress);

        Assert.Single(loggedEntry.Attachments);
        Assert.Equal("test.txt", loggedEntry.Attachments[0].Name);
    }

    [Fact]
    public async Task RunEffects_WithMailEffectAndTriggerContext_LogsTriggerContext()
    {
        var modelService = new ModelService(new ModelParser(new FileSystemProvider("../../../../Examples/Projects")));
        var instanceRepository = new Mock<IWorkflowInstanceRepository>();
        var userService = new Mock<IUserService>();
        var rightsService = new RightsService(modelService, userService.Object, instanceRepository.Object);
        var instanceService =
            new InstanceService(instanceRepository.Object, modelService, userService.Object, rightsService);

        var eventService = new Mock<IInstanceEventService>();
        var mailService = new Mock<IMailService>();
        var artifactService = new Mock<IArtifactService>();
        var mailLogRepository = new Mock<IMailLogRepository>();

        var effectService = new EffectService(
            instanceService,
            eventService.Object,
            modelService,
            mailService.Object,
            artifactService.Object,
            mailLogRepository.Object,
            Options.Create(new GraphMailOptions
            {
                TenantId = "tenant",
                ClientId = "client",
                UserAccount = "user@mail.com",
                OverrideRecipient = null
            }));

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "SendLetter")
            .Build();
        var user = new User { Id = "507f1f77bcf86cd799439011" };

        var mail = new MailMessage("Subject", "Body", null)
        {
            To = [new MailRecipient("to@uva.nl", "To User")]
        };

        MailLogEntry? loggedEntry = null;
        mailLogRepository
            .Setup(r => r.Log(It.IsAny<MailLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MailLogEntry, CancellationToken>((entry, _) => loggedEntry = entry)
            .Returns(Task.CompletedTask);

        var effect = new Effect
        {
            SendMail = new SendMessage { TemplateKey = "DecisionMail" }
        };

        var triggerContext = new MailTriggerContext(MailTriggerType.Action, ActionName: "SendLetter");
        await effectService.RunEffects(instance, [effect], user, CancellationToken.None, mail, triggerContext);

        Assert.NotNull(loggedEntry);
        Assert.Equal("SendLetter", loggedEntry!.StepName);
        Assert.Equal(MailTriggerType.Action, loggedEntry.TriggerType);
        Assert.Equal("SendLetter", loggedEntry.ActionName);
        Assert.Null(loggedEntry.FormId);
        Assert.Equal("DecisionMail", loggedEntry.TemplateKey);
    }
}