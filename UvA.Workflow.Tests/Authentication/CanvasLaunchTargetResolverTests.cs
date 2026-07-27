using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Moq;
using UvA.Workflow.Api.Authentication.CanvasLti;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Authentication;

public class CanvasLaunchTargetResolverTests
{
    private const string TeacherTarget = "/teacher";
    private readonly CancellationToken ct = new CancellationTokenSource().Token;
    private readonly Mock<IWorkflowInstanceRepository> repository = new();
    private readonly ModelService modelService;
    private FilterDefinition<WorkflowInstance>? courseFilter;
    private FilterDefinition<WorkflowInstance>? instanceFilter;

    private readonly User user = new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        UserName = "teacher"
    };

    public CanvasLaunchTargetResolverTests()
    {
        var parser = new ModelParser(new DictionaryProvider(new Dictionary<string, string>()));
        modelService = new ModelService(parser);
    }

    [Fact]
    public async Task ResolveTarget_TeacherWithOneWorkflowDefinition_ReturnsItsFirstScreen()
    {
        AddDefinition("Project", "First", "Second");
        var courseId = ObjectId.GenerateNewId().ToString();
        SetupCourseInstances(
            Course(courseId, "canvas-course"),
            Instance("Project"),
            Instance("Project"));

        var target = await CreateResolver().ResolveTarget(user, TeacherLaunch("canvas-course"), ct);

        Assert.Equal("/screens/Project/First", target);
        Assert.Equal("canvas-course", RenderFilter(courseFilter!)["Properties.ExternalId"]["$in"][0].AsString);
        Assert.Equal(courseId, RenderFilter(instanceFilter!)["Properties.Course"]["$in"][0].AsString);
    }

    [Fact]
    public async Task ResolveTarget_TeacherWithMultipleWorkflowDefinitions_ReturnsTeacherTarget()
    {
        AddDefinition("Project-A", "Projects-A");
        AddDefinition("Project-B", "Projects-B");
        SetupCourseInstances(
            Course(ObjectId.GenerateNewId().ToString(), "canvas-course"),
            Instance("Project-A"),
            Instance("Project-B"));

        var target = await CreateResolver().ResolveTarget(user, TeacherLaunch("canvas-course"), ct);

        Assert.Equal(TeacherTarget, target);
    }

    [Fact]
    public async Task ResolveTarget_TeacherWithNoMatchingCourse_ReturnsTeacherTarget()
    {
        repository.Setup(r => r.GetByWorkflowDefinition(
                "Context",
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var target = await CreateResolver().ResolveTarget(user, TeacherLaunch("canvas-course"), ct);

        Assert.Equal(TeacherTarget, target);
        repository.Verify(r => r.GetByFilter(
            It.IsAny<FilterDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveTarget_TeacherIgnoresWorkflowDefinitionsWithoutScreens()
    {
        AddDefinition("Project", "Projects");
        AddDefinition("Assessment");
        SetupCourseInstances(
            Course(ObjectId.GenerateNewId().ToString(), "canvas-course"),
            Instance("Project"),
            Instance("Assessment"));

        var target = await CreateResolver().ResolveTarget(user, TeacherLaunch("canvas-course"), ct);

        Assert.Equal("/screens/Project/Projects", target);
    }

    [Fact]
    public async Task ResolveTarget_StudentStillReturnsInstanceTarget()
    {
        var instance = Instance("Project");
        repository.Setup(r => r.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([instance]);
        repository.Setup(r => r.GetByIds(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var target = await CreateResolver().ResolveTarget(user, StudentLaunch("canvas-course"), ct);

        Assert.Equal($"/instance/{instance.Id}", target);
        repository.Verify(r => r.GetByWorkflowDefinition(
            It.IsAny<string>(),
            It.IsAny<FilterDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private CanvasLaunchTargetResolver CreateResolver() => new(
        repository.Object,
        modelService,
        Options.Create(new CanvasLtiOptions { TeacherTarget = TeacherTarget }),
        NullLogger<CanvasLaunchTargetResolver>.Instance);

    private void AddDefinition(string name, params string[] screens)
    {
        modelService.WorkflowDefinitions[name] = new WorkflowDefinition
        {
            Name = name,
            Screens = screens.Select(screen => new Screen { Name = screen }).ToList()
        };
    }

    private void SetupCourseInstances(WorkflowInstance course, params WorkflowInstance[] instances)
    {
        repository.Setup(r => r.GetByWorkflowDefinition(
                "Context",
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, FilterDefinition<WorkflowInstance>, CancellationToken>((_, filter, _) =>
                courseFilter = filter)
            .ReturnsAsync([course]);
        repository.Setup(r => r.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<WorkflowInstance>, CancellationToken>((filter, _) =>
                instanceFilter = filter)
            .ReturnsAsync(instances);
    }

    private static CanvasLaunchInfo TeacherLaunch(params string[] courseIdentifiers) =>
        Launch(isTeacher: true, courseIdentifiers);

    private static CanvasLaunchInfo StudentLaunch(params string[] courseIdentifiers) =>
        Launch(isTeacher: false, courseIdentifiers);

    private static CanvasLaunchInfo Launch(bool isTeacher, params string[] courseIdentifiers) => new(
        "teacher",
        "Teacher",
        "teacher@example.com",
        courseIdentifiers,
        isTeacher,
        "en");

    private static WorkflowInstance Course(string id, string externalId) => new()
    {
        Id = id,
        WorkflowDefinition = "Context",
        Properties = new Dictionary<string, BsonValue> { ["ExternalId"] = externalId },
        Events = []
    };

    private static WorkflowInstance Instance(string workflowDefinition) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        WorkflowDefinition = workflowDefinition,
        Properties = [],
        Events = []
    };

    private static BsonDocument RenderFilter(FilterDefinition<WorkflowInstance> filter)
    {
        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var serializer = serializerRegistry.GetSerializer<WorkflowInstance>();
        return filter.Render(new RenderArgs<WorkflowInstance>(serializer,
            serializerRegistry,
            new PathRenderArgs(string.Empty, false),
            renderDollarForm: false,
            renderForFind: false,
            renderForElemMatch: false,
            translationOptions: null));
    }
}