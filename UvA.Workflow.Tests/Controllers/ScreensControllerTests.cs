using Microsoft.AspNetCore.Mvc;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class ScreensControllerTests : ControllerTestsBase
{
    private readonly ScreenDataService _screenDataService;

    public ScreensControllerTests() : base()
    {
        _screenDataService = new ScreenDataService(_modelService, _instanceService, _workflowInstanceRepoMock.Object,
            new InstanceAuthorizationFilterService(_rightsService, _modelService, _userServiceMock.Object,
                _workflowInstanceRepoMock.Object));
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Admin")]
    [InlineData("UnknownRole")]
    public async Task Screens_GetScreenData_AllowAnyone(string role)
    {
        // Arrange
        const string workflowDefinition = "Project";
        const string screenName = "Projects";

        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.GetScreenData(workflowDefinition, screenName, _ct);
        //Assert
        Assert.IsType<ActionResult<ScreenDataDto>>(result);
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Admin")]
    [InlineData("UnknownRole")]
    public async Task Screens_GetGroupedScreenData_AllowAnyone(string role)
    {
        // Arrange
        const string workflowDefinition = "Project";
        const string screenName = "Projects";

        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.GetGroupedScreenData(workflowDefinition, screenName, _ct);
        //Assert
        Assert.IsType<ActionResult<GroupedScreenDataDto>>(result);
    }

    private ScreensController BuildControllerWithRoles(
        string[] roles)
    {
        MockCurrentUser(roles);
        return new ScreensController(_screenDataService);
    }
}