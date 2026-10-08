using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Assessments;
using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests;

public class EffectServiceMailLoggingTests
{
    private readonly ModelService _modelService;
    private readonly Mock<IMailService> _mailService = new();
    private readonly Mock<IArtifactService> _artifactService = new();
    private readonly Mock<IMailLogRepository> _mailLogRepository = new();
    private readonly EffectService _effectService;
    private readonly InstanceService _instanceService;
    private readonly Mock<IAssessmentService> _assessmentService = new();

    private MailLogEntry? _loggedEntry;

    public EffectServiceMailLoggingTests()
    {
        _modelService = new ModelService(new ModelParser(new FileSystemProvider(UnitTestsHelpers.FixturesPath)));

        var instanceRepository = new Mock<IWorkflowInstanceRepository>();
        var userService = new Mock<IUserService>();
        var rightsService = new RightsService(_modelService, userService.Object, instanceRepository.Object);

        var configuration = new Mock<IConfiguration>();
        var mailLayoutResolver = new Mock<IMailLayoutResolver>();
        var mailLayout = new Mock<IMailLayout>();
        mailLayout.Setup(layout => layout.Render(It.IsAny<string>(), It.IsAny<IReadOnlyList<MailButton>>()))
            .Returns((string html, IReadOnlyList<MailButton> _) => html);
        mailLayoutResolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(mailLayout.Object);
        var mailBuilder = UnitTestsHelpers.CreateMailBuilder(mailLayoutResolver.Object, configuration.Object);

        _instanceService = new InstanceService(instanceRepository.Object, _modelService, userService.Object,
            rightsService, mailBuilder, _assessmentService.Object);

        _effectService = new EffectService(
            _instanceService,
            new Mock<IInstanceEventService>().Object,
            _modelService,
            _mailService.Object,
            new Mock<IExternalUserService>().Object,
            _artifactService.Object,
            _mailLogRepository.Object,
            configuration.Object,
            new Mock<IInstanceEventRepository>().Object,
            NullLogger<EffectService>.Instance
        );

        _mailLogRepository
            .Setup(r => r.Log(It.IsAny<MailLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MailLogEntry, CancellationToken>((entry, _) => _loggedEntry = entry)
            .Returns(Task.CompletedTask);
    }

    private static User CreateUser() => new() { Id = "507f1f77bcf86cd799439011" };

    private static WorkflowInstance CreateInstance(string currentStep) =>
        new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: currentStep)
            .Build();

    [Theory]
    [InlineData(5.4f, "FAILURE!")]
    [InlineData(5.5f, "SUCCESS!")]
    [InlineData(6.0f, "SUCCESS!")]
    public async Task RunJob_EnrichesFinalGradeBeforeSelectingResultEmail(float grade, string subject)
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.AssessmentConfiguration = new AssessmentConfiguration();
        _assessmentService.Setup(service => service.GetAssessmentResult(definition,
                It.IsAny<ObjectContext>(), It.IsAny<AssessmentConfiguration>(), null, null))
            .Returns(new AssessmentResult { FinalGradeRounded = grade });
        var effects = new[] { "SUCCESS!", "FAILURE!" }.Select(result =>
        {
            definition.Emails.Add(new TemplateMessage
            {
                Name = result,
                Subject = result,
                Body = "Final grade: {{ Assessment.FinalGrade }}"
            });
            return new Effect
            {
                Condition = new Condition
                {
                    Value = new Value
                    {
                        Property = "Assessment.FinalGrade",
                        GreaterThanOrEqual = result == "SUCCESS!" ? "5.5" : null,
                        LessThan = result == "FAILURE!" ? "5.5" : null
                    }
                },
                SendMail = new SendMessage { TemplateKey = result, To = "student@example.com" }
            };
        }).ToArray();
        _mailService.Setup(service => service.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailDispatchResult([], [], [], null));
        var jobService = new JobService(_effectService, _modelService, Mock.Of<IJobRepository>(),
            Mock.Of<IWorkflowInstanceRepository>(), Mock.Of<IUserRepository>(), NullLogger<JobService>.Instance,
            _instanceService, Options.Create(new WorkerOptions()));

        await jobService.CreateAndRunJob(CreateInstance("Start"), JobSource.Action, "SubmitFinalAssessment",
            effects, CreateUser(), null, CancellationToken.None);

        _mailService.Verify(service => service.Send(It.Is<MailMessage>(mail =>
            mail.Subject == subject && mail.Body.Contains("Final grade:") &&
            mail.To.Single().MailAddress == "student@example.com"), It.IsAny<CancellationToken>()), Times.Once);
        _mailService.Verify(service => service.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(subject, _loggedEntry?.Subject);
    }

    [Fact]
    public async Task RunEffects_WithMailEffect_SendsMailAndLogsFullContent()
    {
        var instance = CreateInstance("Start");
        var user = CreateUser();

        byte[] attachmentBytes = [1, 2, 3, 4];
        var mail = new MailMessage("Subject", "Body", "attachment-template")
        {
            To = [new MailRecipient("to@uva.nl", "To User")],
            Cc = [new MailRecipient("cc@uva.nl", "Cc User")],
            Bcc = [new MailRecipient("bcc@uva.nl", "Bcc User")],
            Attachments = [new MailAttachment("test.txt", attachmentBytes)]
        };
        _mailService.Setup(m => m.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailDispatchResult(mail.To, mail.Cc!, mail.Bcc!, "testen-dn-fnwi@uva.nl"));

        _artifactService
            .Setup(a => a.SaveArtifact(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string artifactId, string name, byte[] _, string _, CancellationToken _) =>
                new ArtifactInfo(artifactId, name));

        var effect = new Effect
        {
            SendMail = new SendMessage()
        };

        await _effectService.RunEffect(new Job { Input = new JobInput(mail) }, instance, effect, user,
            _modelService.CreateContext(instance),
            CancellationToken.None);

        _mailService.Verify(m => m.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        _mailLogRepository.Verify(r => r.Log(It.IsAny<MailLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.NotNull(_loggedEntry);
        Assert.Equal(instance.Id, _loggedEntry!.WorkflowInstanceId);
        Assert.Equal("Project", _loggedEntry.WorkflowDefinition);
        Assert.Equal(user.Id, _loggedEntry.ExecutedBy);
        Assert.Equal("Subject", _loggedEntry.Subject);
        Assert.Equal("Body", _loggedEntry.Body);
        Assert.Equal("attachment-template", _loggedEntry.AttachmentTemplate);

        Assert.Single(_loggedEntry.To);
        Assert.Equal("to@uva.nl", _loggedEntry.To[0].MailAddress);
        Assert.Single(_loggedEntry.Cc);
        Assert.Equal("cc@uva.nl", _loggedEntry.Cc[0].MailAddress);
        Assert.Single(_loggedEntry.Bcc);
        Assert.Equal("bcc@uva.nl", _loggedEntry.Bcc[0].MailAddress);

        Assert.Single(_loggedEntry.Attachments);
        Assert.Equal("test.txt", _loggedEntry.Attachments[0].Name);
    }

    [Fact]
    public async Task RunEffects_WithMailEffectAndTriggerContext_LogsTriggerContext()
    {
        var instance = CreateInstance("SendLetter");

        var mail = new MailMessage("Subject", "Body", null)
        {
            To = [new MailRecipient("to@uva.nl", "To User")]
        };
        _mailService.Setup(m => m.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailDispatchResult(mail.To, [], [], null));

        var effect = new Effect
        {
            SendMail = new SendMessage { TemplateKey = "DecisionMail" }
        };

        await _effectService.RunEffect(new Job { Input = new JobInput(mail) }, instance, effect, CreateUser(),
            _modelService.CreateContext(instance),
            CancellationToken.None);

        Assert.NotNull(_loggedEntry);
    }

    [Fact]
    public async Task RunEffects_WithToastEffect_ReturnsResolvedToast()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Title", b => b.Value("My thesis")))
            .Build();

        var result = await _effectService.RunEffect(
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
            _modelService.CreateContext(instance),
            CancellationToken.None
        );

        Assert.NotNull(result.Toast);
        Assert.Equal(ToastType.Success, result.Toast!.Type);
        Assert.Equal("Saved My thesis", result.Toast.Message.En);
        Assert.Equal("My thesis opgeslagen", result.Toast.Message.Nl);
    }
}