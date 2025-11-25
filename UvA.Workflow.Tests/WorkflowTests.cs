using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Events;
using UvA.Workflow.Persistence;
using UvA.Workflow.Services;
using UvA.Workflow.Submissions;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class WorkflowTests
{
    Mock<IWorkflowInstanceRepository> instanceRepoMock;
    Mock<IInstanceEventRepository> eventRepoMock;
    Mock<IUserService> userServiceMock;
    Mock<IMailService> mailServiceMock;
    Mock<IArtifactService> artifactServiceMock;
    ModelService modelService;
    RightsService rightsService;
    InstanceService instanceService;
    InstanceEventService eventService;
    TriggerService triggerService;
    SubmissionService submissionService;
    ModelParser parser;
    AnswerService answerService;
    AnswerConversionService answerConversionService;
    CancellationToken ct = new CancellationTokenSource().Token;

    public WorkflowTests()
    {
        // Mocks
        instanceRepoMock = new Mock<IWorkflowInstanceRepository>();
        eventRepoMock = new Mock<IInstanceEventRepository>();
        userServiceMock = new Mock<IUserService>();
        mailServiceMock = new Mock<IMailService>();
        artifactServiceMock = new Mock<IArtifactService>();

        // Services
        var modelProvider = new FileSystemProvider("../../../../Examples/Projects");
        parser = new ModelParser(modelProvider);
        modelService = new ModelService(parser);
        rightsService = new RightsService(modelService, userServiceMock.Object);
        instanceService =
            new InstanceService(instanceRepoMock.Object, modelService, userServiceMock.Object, rightsService);
        eventService = new InstanceEventService(eventRepoMock.Object, rightsService, instanceService);
        triggerService = new TriggerService(instanceService, eventService, modelService, mailServiceMock.Object);
        submissionService =
            new SubmissionService(instanceRepoMock.Object, modelService, triggerService, instanceService);
        answerConversionService = new AnswerConversionService(userServiceMock.Object);
        answerService = new AnswerService(submissionService, modelService, instanceService, rightsService,
            artifactServiceMock.Object, answerConversionService);
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

        instanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        JsonElement value = JsonDocument.Parse("\"title\"").RootElement;

        // Act
        var questionContext = await answerService.GetQuestionContext(instance.Id, "Upload", "Title", ct);
        await answerService.SaveAnswer(questionContext, value, ct);

        // Assert
        Assert.Contains(instance.Properties, p => p.Key == "Title" && p.Value.ToString() == "title");
        instanceRepoMock.Verify(
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
        await writer.FlushAsync(ct);
        const string fileName = "test.pdf";

        instanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>())).ReturnsAsync(instance);
        artifactServiceMock.Setup(a => a.SaveArtifact(fileName, It.IsAny<Stream>()))
            .ReturnsAsync(new ArtifactInfo(ObjectId.GenerateNewId(), fileName));

        // Act
        var questionContext = await answerService.GetQuestionContext(instance.Id, "Upload", "Report", ct);
        await answerService.SaveArtifact(questionContext, fileName, ms, ct);

        // Assert
        Assert.Contains(instance.Properties, p => p.Key == "Report");
        var report = BsonSerializer.Deserialize<ArtifactInfo>(instance.Properties["Report"].ToBsonDocument());
        Assert.Equal(fileName, report.Name);
        artifactServiceMock.Verify(a => a.SaveArtifact(fileName, It.IsAny<Stream>()), Times.Once);
        instanceRepoMock.Verify(
            r => r.UpdateFields(instance.Id, It.IsAny<UpdateDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}