using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Serilog;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
using UvA.Workflow.Journaling;
using UvA.Workflow.Persistence;
using UvA.Workflow.Services;
using UvA.Workflow.Submissions;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class WorkflowTests
{
    readonly Mock<IWorkflowInstanceRepository> _instanceRepoMock;
    readonly Mock<IInstanceEventRepository> _eventRepoMock;
    readonly Mock<IUserService> _userServiceMock;
    readonly Mock<IMailService> _mailServiceMock;
    readonly Mock<IArtifactService> _artifactServiceMock;
    readonly Mock<IInstanceJournalService> _instanceJournalServiceMock;
    readonly Mock<IInstanceEventService> _instanceEventService;
    readonly Mock<IConfiguration> _configurationMock;


    readonly ModelService _modelService;
    readonly RightsService _rightsService;
    readonly InstanceService _instanceService;
    readonly WorkflowInstanceService _workflowInstanceService;
    readonly InstanceEventService _eventService;
    readonly EffectService _effectService;
    readonly JobService _jobService;
    readonly SubmissionService _submissionService;
    readonly ModelParser _parser;
    readonly AnswerService _answerService;
    readonly AnswerConversionService _answerConversionService;
    readonly CancellationToken _ct = new CancellationTokenSource().Token;


    public WorkflowTests()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.Debug()
            .CreateLogger();

        // Mocks
        _instanceRepoMock = new Mock<IWorkflowInstanceRepository>();
        _eventRepoMock = new Mock<IInstanceEventRepository>();
        _userServiceMock = new Mock<IUserService>();
        _mailServiceMock = new Mock<IMailService>();
        _artifactServiceMock = new Mock<IArtifactService>();
        _instanceJournalServiceMock = new Mock<IInstanceJournalService>();
        _instanceEventService = new Mock<IInstanceEventService>();
        _configurationMock = new Mock<IConfiguration>();

        // Services
        var modelProvider = new FileSystemProvider("../../../../Examples/Projects");
        _parser = new ModelParser(modelProvider);
        _modelService = new ModelService(_parser);
        _rightsService = new RightsService(_modelService, _userServiceMock.Object, _instanceRepoMock.Object);
        _instanceService =
            new InstanceService(_instanceRepoMock.Object, _modelService, _userServiceMock.Object, _rightsService);
        _eventService = new InstanceEventService(_eventRepoMock.Object, _instanceJournalServiceMock.Object,
            _rightsService,
            _instanceService);
        _workflowInstanceService = new WorkflowInstanceService(_modelService, _instanceRepoMock.Object,
            _instanceJournalServiceMock.Object);
        _effectService = new EffectService(_instanceService, _eventService, _modelService, _mailServiceMock.Object,
            _configurationMock.Object);
        _jobService = new JobService();
        _submissionService =
            new SubmissionService(_instanceRepoMock.Object, _modelService, _effectService, _instanceService,
                _instanceJournalServiceMock.Object, _workflowInstanceService, _effectService, _jobService);
        _answerConversionService = new AnswerConversionService(_userServiceMock.Object);
        _answerService = new AnswerService(_submissionService, _modelService, _instanceService, _rightsService,
            _artifactServiceMock.Object, _answerConversionService, _instanceEventService.Object,
            _instanceJournalServiceMock.Object);
    }

    [Fact]
    public void ExampleBuildingAWorkflowInstance_Success()
    {
        // Arrange
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .WithEvents(
                b => b.WithId("Start").AsCompleted(),
                b => b.WithId("Upload")
            )
            .WithProperties(
                // Simple values
                ("Title", b => b.Value("title")),
                ("EC", b => b.Value(12)),
                ("Supervisor", b => b.Person()),
                ("Report", b => b.FileRef("test.pdf")),

                // Arrays
                ("Reviewers", b => b.Array(3, pb => pb.Person())),
                ("Tester", b => b.Array(pb => pb.Person("Hans"), pb => pb.Person("Max"), pb => pb.Person("Piet"))),
                ("Employees", b => b.Array(3, pb => pb.Person())),
                ("Students", b => b.Array(3, (pb, i) => pb.Person($"student-{i}"))),
                ("Files", b => b.Array(5, (pb, i) => pb.FileRef($"file-{i}.pdf")))
            )
            .Build();
        Assert.NotNull(instance);
    }

    [Fact]
    public async Task SubmitForm_FillAnswer_Success()
    {
        // Arrange
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .WithEvents(b => b.WithId("Start").AsCompleted()
            )
            .Build();

        _instanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        JsonElement value = JsonDocument.Parse("\"title\"").RootElement;

        // Act
        var questionContext = await _answerService.GetQuestionContext(instance.Id, "Upload", "Title", _ct);
        await _answerService.SaveAnswer(questionContext, value, new User(), _ct);

        // Assert
        Assert.Contains(instance.Properties, p => p.Key == "Title" && p.Value.ToString() == "title");
        _instanceRepoMock.Verify(
            r => r.UpdateFields(instance.Id, It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitForm_UploadArtifact_Success()
    {
        // Arrange
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .WithEvents(b => b.WithId("Start").AsCompleted()
            )
            .Build();

        using var ms = new MemoryStream();
        await using var writer = new StreamWriter(ms);
        await writer.WriteLineAsync("This is a test file");
        await writer.FlushAsync(_ct);
        const string fileName = "test.pdf";

        _instanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        _artifactServiceMock.Setup(a => a.SaveArtifact(fileName, It.IsAny<Stream>()))
            .ReturnsAsync(new ArtifactInfo(ObjectId.GenerateNewId(), fileName));

        // Act
        var questionContext = await _answerService.GetQuestionContext(instance.Id, "Upload", "Report", _ct);
        await _answerService.SaveArtifact(questionContext, fileName, ms, _ct);

        // Assert
        Assert.Contains(instance.Properties, p => p.Key == "Report");
        var report = BsonSerializer.Deserialize<ArtifactInfo>(instance.Properties["Report"].ToBsonDocument());
        Assert.Equal(fileName, report.Name);
        _artifactServiceMock.Verify(a => a.SaveArtifact(fileName, It.IsAny<Stream>()), Times.Once);
        _instanceRepoMock.Verify(
            r => r.UpdateFields(instance.Id, It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}