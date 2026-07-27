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
        var createdOn = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var project = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Project")
            .WithCurrentStep("Upload")
            .WithProperties(
                ("Title", property => property.Value("Personal project")),
                ("Student", property => property.Value(UserDocument(userId))),
                ("Supervisor", property => property.Value(UserDocument(userId))))
            .Build();
        project.CreatedOn = createdOn;
        var context = new WorkflowInstanceBuilder()
            .WithWorkflowDefinition("Context")
            .WithCurrentStep("Active")
            .WithProperties(
                ("Name", property => property.Value("Personal context")),
                ("Coordinator", property => property.Value(new BsonArray
                {
                    UserDocument(ObjectId.GenerateNewId()),
                    UserDocument(userId)
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
        MockCurrentUser();

        var controller = new PersonalController(_userServiceMock.Object, personalInstanceService);
        var result = await controller.GetInstances(_ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rows = Assert.IsType<PersonalInstanceDto[]>(ok.Value);
        Assert.Equal(2, rows.Length);

        var projectRow = rows[0];
        Assert.Equal(project.Id, projectRow.Id);
        Assert.Equal("Project", projectRow.WorkflowDefinition);
        Assert.Equal("Personal project", projectRow.Title);
        Assert.Equal("Upload", projectRow.CurrentStep);
        Assert.Equal(createdOn, projectRow.CreatedOn);
        Assert.Equal(["Student", "Supervisor"], projectRow.Roles);

        var contextRow = rows[1];
        Assert.Equal(context.Id, contextRow.Id);
        Assert.Equal("Context", contextRow.WorkflowDefinition);
        Assert.Equal(["Coordinator"], contextRow.Roles);

        _workflowInstanceRepoMock.Verify(repository => repository.GetByFilter(
            It.IsAny<FilterDefinition<WorkflowInstance>>(),
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

    private static BsonDocument UserDocument(ObjectId id) => new("_id", id);

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