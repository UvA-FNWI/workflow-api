using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.WorkflowDefinitions;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class WorkflowDefinitionsControllerTests : ControllerTestsBase
{
    private WorkflowDefinitionsController BuildController(params string[] roles)
    {
        MockCurrentUser(roles);
        var authorizationFilterService = new InstanceAuthorizationFilterService(
            _rightsService, _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);
        return new WorkflowDefinitionsController(_modelService, _rightsService, authorizationFilterService);
    }

    private static List<WorkflowDefinitionDto> GetDtos(ActionResult<IEnumerable<WorkflowDefinitionDto>> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<IEnumerable<WorkflowDefinitionDto>>(ok.Value).ToList();
    }

    [Fact]
    public async Task GetAll_Default_OnlyReturnsDefinitionsWithScreens()
    {
        var controller = BuildController("Coordinator");

        var dtos = GetDtos(await controller.GetAll());

        Assert.NotEmpty(dtos);
        Assert.All(dtos, d => Assert.NotEmpty(d.Screens));
        // "Assessment" has no screens, so it must be excluded by default
        Assert.DoesNotContain(dtos, d => d.Name == "Assessment");
    }

    [Fact]
    public async Task GetAll_IncludeAll_IncludesDefinitionsWithoutScreens()
    {
        var controller = BuildController("Coordinator");

        var defaultDtos = GetDtos(await controller.GetAll());
        var allDtos = GetDtos(await controller.GetAll(includeAll: true));

        Assert.True(allDtos.Count > defaultDtos.Count);
        Assert.Contains(allDtos, d => d.Screens.Length == 0);
        Assert.True(Assert.Single(allDtos, d => d.Name == "Assessment").IsPropertyOnly);
        Assert.False(Assert.Single(allDtos, d => d.Name == "Project").IsPropertyOnly);
    }

    [Fact]
    public async Task GetAll_SetsCanCreateInstance_ForDefinitionsTheUserCanCreate()
    {
        // The "Project" definition grants CreateInstance to the always-present "Registered" role.
        var controller = BuildController();

        var dtos = GetDtos(await controller.GetAll(includeAll: true));

        var project = Assert.Single(dtos, d => d.Name == "Project");
        Assert.True(project.CanCreateInstance);

        // "Context" is only creatable by the "Api" role, which this user does not have.
        var context = Assert.Single(dtos, d => d.Name == "Context");
        Assert.False(context.CanCreateInstance);
    }

    [Fact]
    public async Task GetAll_CanCreateInstance_RespectsRole()
    {
        // The "Api" role grants CreateInstance for "Context".
        var controller = BuildController("Api");

        var dtos = GetDtos(await controller.GetAll(includeAll: true));

        var context = Assert.Single(dtos, d => d.Name == "Context");
        Assert.True(context.CanCreateInstance);
    }

    [Fact]
    public async Task GetAccessible_ReturnsOnlyDefinitionsWithInheritedAccess()
    {
        var controller = BuildController();
        MockNoVisibleInstances();
        var courseId = ObjectId.GenerateNewId();
        MockVisibleInstancesFor("Course",
            new BsonDocument("Properties.Coordinator._id", ObjectId.Parse(UnitTestsHelpers.AdminUser.Id)),
            courseId);
        MockVisibleInstancesFor("Project-PP",
            new BsonDocument("Properties.Course", new BsonDocument("$in", new BsonArray { courseId.ToString() })),
            ObjectId.GenerateNewId());

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.All(dtos, d => Assert.NotEmpty(d.Screens));
        Assert.Contains(dtos, d => d.Name == "Project-PP");
        Assert.DoesNotContain(dtos, d => d.Name == "Project-RMSS");
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Supervisor")]
    public async Task GetAccessible_ExcludesDirectRoles_WithoutRemovingInstanceAccess(string role)
    {
        var controller = BuildController();
        MockNoVisibleInstances();
        var directFilter = new BsonDocument($"Properties.{role}._id",
            ObjectId.Parse(UnitTestsHelpers.AdminUser.Id));
        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                "Project",
                It.IsAny<Dictionary<string, string>>(),
                It.Is<BsonDocument?>(filter => filter != null && filter.ToJson().Contains($"Properties.{role}._id")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = ObjectId.GenerateNewId() }]);

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Empty(dtos);
        var authorization = new InstanceAuthorizationFilterService(
            _rightsService, _modelService, _userServiceMock.Object, _workflowInstanceRepoMock.Object);
        var instanceFilter = await authorization.BuildAuthorizationFilter("Project", RoleAction.View, _ct);
        Assert.Contains(directFilter.ToJson(), instanceFilter!.ToJson());
    }

    [Theory]
    [InlineData("Coordinator")]
    [InlineData("SystemAdmin")]
    public async Task GetAccessible_IncludesGlobalViewRoles_WithoutInstances(string role)
    {
        var controller = BuildController(role);
        MockNoVisibleInstances();

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Contains(dtos, d => d.Name == "Project");
    }

    [Fact]
    public async Task GetAccessible_ReturnsEmpty_WithoutGlobalOrInheritedAccess()
    {
        var controller = BuildController();
        MockNoVisibleInstances();

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Empty(dtos);
    }

    [Fact]
    public async Task GetAccessible_ReturnsEmpty_ForUnauthenticatedUser()
    {
        var controller = BuildController();
        MockNoVisibleInstances();
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((UvA.Workflow.Users.User?)null);

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Empty(dtos);
    }

    [Fact]
    public async Task GetAccessible_ReturnsGroupedChildScreens()
    {
        var controller = BuildController("SystemAdmin");
        MockNoVisibleInstances();
        var courseId = ObjectId.GenerateNewId();
        var programmeId = ObjectId.GenerateNewId();
        var facultyId = ObjectId.GenerateNewId();
        MockVisibleInstancesFor("Faculty",
            new BsonDocument("Properties.LocalAdmin._id", ObjectId.Parse(UnitTestsHelpers.AdminUser.Id)),
            facultyId);
        MockVisibleInstancesFor("Programme",
            new BsonDocument("Properties.Faculty", facultyId),
            programmeId);
        MockVisibleInstancesFor("Course",
            new BsonDocument("Properties.Programme", programmeId),
            courseId);

        MockVisibleInstancesFor("Project",
            new BsonDocument("Properties.Course", new BsonDocument("$in", new BsonArray { courseId.ToString() })),
            ObjectId.GenerateNewId());

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.Contains(dtos, d => d.Name == "Context" && d.Screens.Length == 1 && d.Screens[0] == "Default");
        Assert.Contains(dtos, d => d.Name == "Project");
        Assert.DoesNotContain(dtos, d =>
            d.Name is "Course" or "Programme" or "Faculty" or "Specialisation");
    }

    private void MockNoVisibleInstances() =>
        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

    private void MockVisibleInstancesFor(string workflowDefinition, BsonDocument filter, ObjectId id) =>
        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                workflowDefinition,
                It.IsAny<Dictionary<string, string>>(),
                It.Is<BsonDocument?>(actual => actual != null && actual.Equals(filter)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = id }]);
}