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
    public async Task GetInstances_ReturnsDirectUserInstancesWithTheirMatchingRoles()
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
        var context = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Context")
            .WithCurrentStep("Active")
            .WithProperties(
                ("Name", property => property.Value("Personal context")),
                ("Coordinator", property => property.Value(new BsonArray
                {
                    UserDocument(ObjectId.GenerateNewId(), "Other Coordinator"),
                    UserDocument(userId, "Current Employee")
                })))
            .Build();
        context.CreatedOn = createdOn.AddDays(-1);

        FilterDefinition<WorkflowInstance>? capturedFilter = null;
        _workflowInstanceRepoMock
            .Setup(repository => repository.GetByFilter(
                It.IsAny<FilterDefinition<WorkflowInstance>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<WorkflowInstance>, CancellationToken>((filter, _) =>
                capturedFilter = filter)
            .ReturnsAsync([project, context]);
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
        Assert.Equal(2, rows.Length);
        Assert.Equal(["Coordinator", "Reviewer", "Supervisor"], response.Roles.Select(role => role.Name));
        Assert.Equal("Coördinator", response.Roles[0].Title.Nl);
        Assert.Equal("Beoordelaar", response.Roles[1].Title.Nl);
        Assert.Equal("Begeleider", response.Roles[2].Title.Nl);

        var projectRow = rows[0];
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
        Assert.Equal(["Current Employee", "Examiner Name"], projectRow.Employees);

        var contextRow = rows[1];
        Assert.Equal(context.Id, contextRow.Id);
        Assert.Equal("Context", contextRow.WorkflowDefinition);
        Assert.Equal(["Coordinator"], contextRow.Roles);
        Assert.Null(contextRow.Student);
        Assert.Null(contextRow.Course);
        Assert.Equal(["Current Employee", "Other Coordinator"], contextRow.Employees);

        _workflowInstanceRepoMock.Verify(repository => repository.GetByFilter(
            It.IsAny<FilterDefinition<WorkflowInstance>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _workflowInstanceRepoMock.Verify(repository => repository.GetAllById(
            It.IsAny<string[]>(),
            It.Is<Dictionary<string, string>>(projection =>
                projection.Count == 1 && projection["Name"] == "$Properties.Name"),
            It.IsAny<CancellationToken>()), Times.Once);

        var renderedFilter = RenderFilter(capturedFilter!);
        AssertDirectBinding(renderedFilter, "Project", "Properties.Student._id", userId);
        AssertDirectBinding(renderedFilter, "Context", "Properties.Coordinator._id", userId);
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

    private static void AssertDirectBinding(
        BsonDocument filter,
        string workflowDefinition,
        string userPath,
        ObjectId userId)
    {
        var found = filter["$or"].AsBsonArray
            .Select(branch => branch.AsBsonDocument)
            .Any(branch =>
                branch.GetValue("WorkflowDefinition", BsonNull.Value) == workflowDefinition &&
                branch.GetValue(userPath, BsonNull.Value) == userId);

        Assert.True(found, $"No query branch found for {workflowDefinition}.{userPath}");
    }
}