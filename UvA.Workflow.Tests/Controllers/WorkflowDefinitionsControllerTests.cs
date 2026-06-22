using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.WorkflowDefinitions;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;

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
        Assert.Contains(allDtos, d => d.Name == "Assessment");
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
    public async Task GetAccessible_ReturnsOnlyDefinitionsTheUserHasVisibleInstancesFor()
    {
        // No global roles, so the user has no unconditional view access; access is decided purely
        // by instance membership (the filter the screens use). The repository returns instances
        // only for "Project-PP", so that should be the only course the user can access.
        var controller = BuildController();
        MockNoVisibleInstances();
        MockVisibleInstancesFor("Project-PP");

        var dtos = GetDtos(await controller.GetAccessible(_ct));

        Assert.All(dtos, d => Assert.NotEmpty(d.Screens));
        Assert.Contains(dtos, d => d.Name == "Project-PP");
        Assert.DoesNotContain(dtos, d => d.Name == "Project-RMSS");
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

    private void MockNoVisibleInstances() =>
        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

    private void MockVisibleInstancesFor(string workflowDefinition) =>
        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                workflowDefinition,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Dictionary<string, BsonValue> { ["_id"] = ObjectId.GenerateNewId() }]);
}