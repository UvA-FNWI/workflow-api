using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
using UvA.Workflow.Journaling;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Impersonation;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public abstract class ControllerTestsBase
{
    protected readonly Mock<IWorkflowInstanceRepository> _instanceRepoMock;
    protected readonly Mock<IInstanceEventRepository> _eventRepoMock;
    protected readonly Mock<IUserService> _userServiceMock;
    protected readonly Mock<IMailService> _mailServiceMock;
    protected readonly Mock<IMailLogRepository> _mailLogRepositoryMock;
    protected readonly Mock<IArtifactService> _artifactServiceMock;
    protected readonly Mock<IInstanceJournalService> _instanceJournalServiceMock;
    protected readonly Mock<IInstanceEventService> _instanceEventService;
    protected readonly Mock<IJobRepository> _jobRepositoryMock;
    protected readonly Mock<IUserRepository> _userRepoMock;
    protected readonly Mock<IConfiguration> _configurationMock;

    protected readonly ModelService _modelService;
    protected readonly RightsService _rightsService;
    protected readonly InstanceService _instanceService;
    protected readonly WorkflowInstanceService _workflowInstanceService;
    protected readonly InstanceEventService _eventService;
    protected readonly EffectService _effectService;
    protected readonly JobService _jobService;

    protected readonly CancellationToken _ct = new CancellationTokenSource().Token;

    protected ControllerTestsBase() : base()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.Debug()
            .CreateLogger();
        var factory = LoggerFactory.Create(builder => { builder.AddSerilog(Log.Logger, dispose: true); });

        // Mocks
        _instanceRepoMock = new Mock<IWorkflowInstanceRepository>();
        _eventRepoMock = new Mock<IInstanceEventRepository>();
        _userServiceMock = new Mock<IUserService>();
        _mailServiceMock = new Mock<IMailService>();
        _mailLogRepositoryMock = new Mock<IMailLogRepository>();
        _artifactServiceMock = new Mock<IArtifactService>();
        _instanceJournalServiceMock = new Mock<IInstanceJournalService>();
        _instanceEventService = new Mock<IInstanceEventService>();
        _configurationMock = new Mock<IConfiguration>();
        _userRepoMock = new Mock<IUserRepository>();
        _jobRepositoryMock = new Mock<IJobRepository>();

        // Services
        _modelService = ControllerTestsHelpers.CreateModelService();
        _rightsService = new RightsService(_modelService, _userServiceMock.Object, _instanceRepoMock.Object);
        _configurationMock.SetupGet(c => c["FileKey"]).Returns(ImpersonationTestHelpers.SigningKey);

        var mailLayoutResolver = new Mock<IMailLayoutResolver>();
        mailLayoutResolver.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(new Mock<IMailLayout>().Object);
        var mailBuilder = new MailBuilder(mailLayoutResolver.Object, _configurationMock.Object);

        _instanceService =
            new InstanceService(_instanceRepoMock.Object, _modelService, _userServiceMock.Object, _rightsService,
                mailBuilder);

        _eventService =
            new InstanceEventService(_eventRepoMock.Object, _instanceJournalServiceMock.Object, _instanceService);

        _workflowInstanceService = new WorkflowInstanceService(_modelService, _instanceRepoMock.Object,
            _instanceJournalServiceMock.Object);

        _effectService = new EffectService(_instanceService, _eventService, _modelService, _mailServiceMock.Object,
            mailBuilder, _artifactServiceMock.Object,
            _mailLogRepositoryMock.Object, Options.Create(new GraphMailOptions
            {
                TenantId = "tenant",
                ClientId = "client",
                UserAccount = "user@mail.com",
            }), _configurationMock.Object);

        _jobService = new JobService(_effectService, _modelService, _jobRepositoryMock.Object,
            _instanceRepoMock.Object, userRepository: _userRepoMock.Object, factory.CreateLogger<JobService>(),
            _instanceService);
    }
}