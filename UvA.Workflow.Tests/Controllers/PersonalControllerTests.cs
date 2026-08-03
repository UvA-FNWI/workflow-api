using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Api.Personal;
using UvA.Workflow.Api.Personal.Dtos;
using UvA.Workflow.Tests.Builders;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class PersonalControllerTests : ControllerTestsBase
{
    private readonly PersonalInstanceService personalInstanceService;

    public PersonalControllerTests()
    {
        personalInstanceService = new PersonalInstanceService(_modelService, _workflowInstanceRepoMock.Object);
    }

    [Fact]
    public async Task GetInstances_ReturnsDirectUserInstancesTheUserCanView()
    {
        var userId = ObjectId.Parse(UnitTestsHelpers.AdminUser.Id);
        var courseId = ObjectId.GenerateNewId();
        var createdOn = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var project = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Upload")
            .WithProperties(
                ("Title", property => property.Value("Personal project")),
                ("Course", property => property.Value(courseId.ToString())),
                ("Student", property => property.Value(
                    UserDocument(ObjectId.GenerateNewId(), "Student Name"))),
                ("Supervisor", property => property.Value(
                    UserDocument(userId, "Current Employee"))),
                ("Reviewer", property => property.Value(
                    UserDocument(userId, "Current Employee"))),
                ("Examiner", property => property.Value(
                    UserDocument(ObjectId.GenerateNewId(), "Examiner Name"))))
            .Build();
        project.CreatedOn = createdOn;

        FilterDefinition<WorkflowInstance>? capturedFilter = null;
        _workflowInstanceRepoMock
            .Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<WorkflowInstance>, CancellationToken>((filter, _) =>
                capturedFilter = filter)
            .ReturnsAsync([project]);
        _workflowInstanceRepoMock
            .Setup(repository => repository.GetAllById(
                It.Is<string[]>(ids => ids.SequenceEqual(new[] { courseId.ToString() })),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Dictionary<string, BsonValue>
                {
                    ["_id"] = courseId,
                    ["Name"] = "Software Engineering"
                }
            ]);
        MockCurrentUser();

        var controller = new PersonalController(_userServiceMock.Object, personalInstanceService);
        var result = await controller.GetInstances(_ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PersonalInstancesDto>(ok.Value);
        var rows = response.Instances;
        var projectRow = Assert.Single(rows);
        Assert.Equal(["Supervisor", "Reviewer"], response.Roles.Select(role => role.Name));
        Assert.Equal("Begeleider", response.Roles[0].Title.Nl);
        Assert.Equal("Beoordelaar", response.Roles[1].Title.Nl);

        Assert.Equal(project.Id, projectRow.Id);
        Assert.Equal("Project", projectRow.WorkflowDefinition);
        Assert.Equal("Personal project", projectRow.Title);
        Assert.Equal("Upload", projectRow.CurrentStep);
        Assert.Equal("Final version", projectRow.Progress.Text.En);
        Assert.Equal("Eindversie", projectRow.Progress.Text.Nl);
        Assert.Equal(StatusColor.Green, projectRow.Progress.Color);
        Assert.Equal(createdOn, projectRow.CreatedOn);
        Assert.Equal(["Reviewer", "Supervisor"], projectRow.Roles);
        Assert.Equal("Student Name", projectRow.Student);
        Assert.Equal("Software Engineering", projectRow.Course);
        Assert.Equal(["Examiner Name"], projectRow.Employees);

        _workflowInstanceRepoMock.Verify(repository => repository.GetByFilter(
            It.IsAny<FilterDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _workflowInstanceRepoMock.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.Is<Dictionary<string, string>>(projection =>
                projection.Count == 1 && projection["Name"] == "$Properties.Name"),
            It.IsAny<CancellationToken>()), Times.Once);

        var renderedFilter = RenderFilter(capturedFilter!);
        AssertDirectPropertyFilter(renderedFilter, "Project", "Properties.Student._id", userId);
        AssertDirectPropertyFilter(renderedFilter, "Project", "Properties.Supervisor._id", userId);
        AssertDirectPropertyFilter(renderedFilter, "Project", "Properties.Reviewer._id", userId);
        AssertDefinitionIsExcluded(renderedFilter, "Context");
    }

    [Fact]
    public async Task GetInstances_WithoutCurrentUser_ReturnsUnauthorizedWithoutQueryingMongo()
    {
        _userServiceMock
            .Setup(service => service.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var controller = new PersonalController(_userServiceMock.Object, personalInstanceService);

        var result = await controller.GetInstances(_ct);

        Assert.IsType<UnauthorizedResult>(result.Result);
        _workflowInstanceRepoMock.Verify(repository => repository.GetByFilter(
            It.IsAny<FilterDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static BsonDocument UserDocument(ObjectId id, string displayName) => new()
    {
        ["_id"] = id,
        ["DisplayName"] = displayName
    };

    private static BsonDocument RenderFilter(FilterDefinition<WorkflowInstance> filter)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<WorkflowInstance>();
        return filter.Render(new RenderArgs<WorkflowInstance>(
            serializer,
            BsonSerializer.SerializerRegistry,
            new PathRenderArgs(string.Empty, false),
            renderDollarForm: false));
    }

    private static void AssertDirectPropertyFilter(
        BsonDocument filter,
        string workflowDefinition,
        string userPath,
        ObjectId userId)
    {
        var found = filter["$or"].AsBsonArray
            .Select(branch => branch.AsBsonDocument)
            .Any(branch =>
                Descendants(branch).Any(condition =>
                    condition.GetValue("WorkflowDefinition", BsonNull.Value) == workflowDefinition) &&
                Descendants(branch).Any(condition =>
                    condition.GetValue(userPath, BsonNull.Value) == userId));

        Assert.True(found, $"No query branch found for {workflowDefinition}.{userPath}");
    }

    private static void AssertDefinitionIsExcluded(BsonDocument filter, string workflowDefinition)
    {
        var found = filter["$or"].AsBsonArray
            .Select(value => value.AsBsonDocument)
            .Any(branch => Descendants(branch).Any(condition =>
                condition.GetValue("WorkflowDefinition", BsonNull.Value) == workflowDefinition));

        Assert.False(found, $"The {workflowDefinition} query branch should not be present");
    }

    private static IEnumerable<BsonDocument> Descendants(BsonValue value)
    {
        if (value is BsonDocument document)
        {
            yield return document;
            foreach (var descendant in document.Values.SelectMany(Descendants))
                yield return descendant;
        }
        else if (value is BsonArray array)
        {
            foreach (var descendant in array.SelectMany(Descendants))
                yield return descendant;
        }
    }
}