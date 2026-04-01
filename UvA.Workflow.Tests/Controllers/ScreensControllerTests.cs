using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Screens.Dtos;

namespace UvA.Workflow.Tests.Controllers;

public class ScreensControllerTests : ControllerTestsBase
{
    private readonly ScreenDataService _screenDataService;

    public ScreensControllerTests() : base()
    {
        _screenDataService = new ScreenDataService(_modelService, _instanceService, _instanceRepoMock.Object,
            new InstanceAuthorizationFilterService(_rightsService, _modelService, _userServiceMock.Object,
                _instanceRepoMock.Object));
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
        _userServiceMock.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ControllerTestsHelpers.AdminUser);

        var controller = new ScreensController(_screenDataService, _rightsService);

        return controller;
    }
}