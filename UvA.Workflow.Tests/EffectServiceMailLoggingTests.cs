using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
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
        var eventService = new Mock<IInstanceEventService>();
        var mailService = new Mock<IMailService>();
        var artifactService = new Mock<IArtifactService>();
        var mailLogRepository = new Mock<IMailLogRepository>();

        var configuration = new Mock<IConfiguration>();
        var mailLayoutResolver = new Mock<IMailLayoutResolver>();
        mailLayoutResolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(new Mock<IMailLayout>().Object);
        var mailBuilder = new MailBuilder(mailLayoutResolver.Object, configuration.Object);
        var instanceService =
            new InstanceService(instanceRepository.Object, modelService, userService.Object, rightsService,
                mailBuilder);

        var effectService = new EffectService(
            instanceService,
            eventService.Object,
            modelService,
            mailService.Object,
            mailBuilder,
            artifactService.Object,
            mailLogRepository.Object,
            Options.Create(new GraphMailOptions
            {
                TenantId = "tenant",
                ClientId = "client",
                UserAccount = "user@mail.com",
                OverrideRecipient = "testen-dn-fnwi@uva.nl"
            }),
            configuration.Object,
            NullLogger<EffectService>.Instance
        );

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

        await effectService.RunEffect(new Job { Input = new JobInput(mail) }, instance, effect, user,
            modelService.CreateContext(instance),
            CancellationToken.None);

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
        var eventService = new Mock<IInstanceEventService>();
        var mailService = new Mock<IMailService>();
        var artifactService = new Mock<IArtifactService>();
        var mailLogRepository = new Mock<IMailLogRepository>();

        var configuration = new Mock<IConfiguration>();
        var mailLayoutResolver = new Mock<IMailLayoutResolver>();
        mailLayoutResolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(new Mock<IMailLayout>().Object);
        var mailBuilder = new MailBuilder(mailLayoutResolver.Object, configuration.Object);
        var instanceService =
            new InstanceService(instanceRepository.Object, modelService, userService.Object, rightsService,
                mailBuilder);
        var effectService = new EffectService(
            instanceService,
            eventService.Object,
            modelService,
            mailService.Object,
            mailBuilder,
            artifactService.Object,
            mailLogRepository.Object,
            Options.Create(new GraphMailOptions
            {
                TenantId = "tenant",
                ClientId = "client",
                UserAccount = "user@mail.com",
                OverrideRecipient = null
            }),
            configuration.Object,
            NullLogger<EffectService>.Instance);

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

        await effectService.RunEffect(new Job { Input = new JobInput(mail) }, instance, effect, user,
            modelService.CreateContext(instance),
            CancellationToken.None);

        Assert.NotNull(loggedEntry);
    }

    [Fact]
    public async Task RunEffects_WithToastEffect_ReturnsResolvedToast()
    {
        var modelService = new ModelService(new ModelParser(new FileSystemProvider("../../../../Examples/Projects")));
        var instanceRepository = new Mock<IWorkflowInstanceRepository>();
        var userService = new Mock<IUserService>();
        var rightsService = new RightsService(modelService, userService.Object, instanceRepository.Object);
        var eventService = new Mock<IInstanceEventService>();
        var mailService = new Mock<IMailService>();
        var artifactService = new Mock<IArtifactService>();
        var mailLogRepository = new Mock<IMailLogRepository>();

        var configuration = new Mock<IConfiguration>();
        var mailLayoutResolver = new Mock<IMailLayoutResolver>();
        mailLayoutResolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(new Mock<IMailLayout>().Object);
        var mailBuilder = new MailBuilder(mailLayoutResolver.Object, configuration.Object);
        var instanceService =
            new InstanceService(instanceRepository.Object, modelService, userService.Object, rightsService,
                mailBuilder);

        var effectService = new EffectService(
            instanceService,
            eventService.Object,
            modelService,
            mailService.Object,
            mailBuilder,
            artifactService.Object,
            mailLogRepository.Object,
            Options.Create(new GraphMailOptions
            {
                TenantId = "tenant",
                ClientId = "client",
                UserAccount = "user@mail.com",
            }),
            configuration.Object,
            NullLogger<EffectService>.Instance
        );

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Title", b => b.Value("My thesis")))
            .Build();

        var result = await effectService.RunEffect(
            new Job(),
            instance,
            new Effect
            {
                Toast = new Toast
                {
                    Type = ToastType.Success,
                    Message = new BilingualString("Saved {{Title}}", "{{Title}} opgeslagen")
                }
            },
            new User(),
            modelService.CreateContext(instance),
            CancellationToken.None
        );

        Assert.NotNull(result.Toast);
        Assert.Equal(ToastType.Success, result.Toast!.Type);
        Assert.Equal("Saved My thesis", result.Toast.Message.En);
        Assert.Equal("My thesis opgeslagen", result.Toast.Message.Nl);
    }
}