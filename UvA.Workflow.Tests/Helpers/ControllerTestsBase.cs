using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure.S3;
using UvA.Workflow.Jobs;
using UvA.Workflow.Journaling;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Tests.Impersonation;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers.Helpers;

public abstract class ControllerTestsBase
{
    protected readonly Mock<IWorkflowInstanceRepository> _workflowInstanceRepoMock;
    protected readonly Mock<IInstanceEventRepository> _eventRepoMock;
    protected readonly Mock<IUserService> _userServiceMock;
    protected readonly Mock<IMailService> _mailServiceMock;
    protected readonly Mock<IEduIdUserService> _eduIdUserServiceMock;
    protected readonly Mock<IMailLogRepository> _mailLogRepositoryMock;
    protected readonly Mock<IArtifactService> _artifactServiceMock;
    protected readonly Mock<IInstanceJournalService> _instanceJournalServiceMock;
    protected readonly Mock<IInstanceEventService> _instanceEventService;
    protected readonly Mock<IJobRepository> _jobRepositoryMock;
    protected readonly Mock<IUserRepository> _userRepoMock;
    protected readonly Mock<IConfiguration> _configurationMock;

    protected readonly ILoggerFactory _loggerFactory;

    protected readonly ModelParser _modelParser;
    protected readonly ModelService _modelService;
    protected readonly RightsService _rightsService;
    protected readonly InstanceService _instanceService;
    protected readonly WorkflowInstanceService _workflowInstanceService;
    protected readonly InstanceEventService _eventService;
    protected readonly EffectService _effectService;
    protected readonly JobService _jobService;

    protected readonly CancellationToken _ct = new CancellationTokenSource().Token;

    protected readonly IOptionsMonitor<S3Config> _s3OptionsMonitor =
        new UnitTestsHelpers.TestOptionsMonitor<S3Config>(new S3Config
        {
            ServiceUrl = "http://serviceurl",
            AuthenticationRegion = "EU",
            AccessKey = "zh7F5ZZxmchb3We49nGVMhESZhRtxhWuZhQCDSQak5M", // Dummy AccessKey
            SecretKey = "LaIhdtuPhgkbczwo9ZcDkFI5E6Cdn7QoN30nP3LUQgM", // Dummy SecretKey
            SigningKey = "criXzMbgewG6VC1ebBcmSN92bl496oc0xNOaM6cCS7e" // Dummy SigningKey
        });

    protected ControllerTestsBase() : base()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.Debug()
            .CreateLogger();

        _loggerFactory = LoggerFactory.Create(builder => { builder.AddSerilog(Log.Logger, dispose: true); });

        // Mocks
        _workflowInstanceRepoMock = new Mock<IWorkflowInstanceRepository>();
        _eventRepoMock = new Mock<IInstanceEventRepository>();
        _userServiceMock = new Mock<IUserService>();
        _mailServiceMock = new Mock<IMailService>();
        _eduIdUserServiceMock = new Mock<IEduIdUserService>();
        _mailLogRepositoryMock = new Mock<IMailLogRepository>();
        _artifactServiceMock = new Mock<IArtifactService>();
        _instanceJournalServiceMock = new Mock<IInstanceJournalService>();
        _instanceEventService = new Mock<IInstanceEventService>();
        _jobRepositoryMock = new Mock<IJobRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _jobRepositoryMock.Setup(r => r.Add(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _configurationMock = new Mock<IConfiguration>();
        _configurationMock.SetupGet(c => c["FileKey"]).Returns(ImpersonationTestHelpers.SigningKey);
        _mailServiceMock.Setup(m => m.Send(It.IsAny<MailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MailDispatchResult([], [], [], null));
        _mailLogRepositoryMock.Setup(r => r.Log(It.IsAny<MailLogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _modelParser = UnitTestsHelpers.CreateModelParser();

        // Services
        _modelService = new ModelService(_modelParser);
        _rightsService = new RightsService(_modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);

        var mailLayoutResolver = new Mock<IMailLayoutResolver>();
        mailLayoutResolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(new Mock<IMailLayout>().Object);
        var mailBuilder = new MailBuilder(mailLayoutResolver.Object, _configurationMock.Object);

        _instanceService =
            new InstanceService(_workflowInstanceRepoMock.Object, _modelService, _userServiceMock.Object,
                _rightsService,
                mailBuilder);

        _eventService =
            new InstanceEventService(_eventRepoMock.Object, _instanceJournalServiceMock.Object, _instanceService);

        _workflowInstanceService =
            new WorkflowInstanceService(_modelService, _workflowInstanceRepoMock.Object,
                _instanceJournalServiceMock.Object);

        _effectService =
            new EffectService(_instanceService,
                _eventService,
                _modelService,
                _mailServiceMock.Object,
                _eduIdUserServiceMock.Object,
                mailBuilder,
                _artifactServiceMock.Object,
                _mailLogRepositoryMock.Object,
                _configurationMock.Object,
                _loggerFactory.CreateLogger<EffectService>());

        _jobService =
            new JobService(_effectService, _modelService, _jobRepositoryMock.Object,
                _workflowInstanceRepoMock.Object, userRepository: _userRepoMock.Object,
                _loggerFactory.CreateLogger<JobService>(),
                _instanceService);
    }

    protected void MockCurrentUser(params string[] roles)
    {
        _userServiceMock.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnitTestsHelpers.AdminUser);
    }

    protected void MockInstance(WorkflowInstance instance)
    {
        _workflowInstanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
    }

    protected void MockEmptyRelatedInstanceLookups()
    {
        _workflowInstanceRepoMock.Setup(r => r.GetAllById(It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    protected void MockEmptyEventLog(WorkflowInstance instance) => MockEventLogs(instance, []);

    protected void MockEventLogs(WorkflowInstance instance, List<InstanceEventLogEntry> eventLog)
    {
        _eventRepoMock.Setup(r => r.GetEventLogEntriesForInstance(instance.Id,
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(eventLog);
    }
}