using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.WorkflowInstances;

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

    [Fact]
    public async Task Screens_GetScreenData_NonGroupedScreen_ReturnsFlatRows()
    {
        // Arrange
        const string workflowDefinition = "Context";
        const string screenName = "Default";

        var controller = BuildControllerWithRoles(["Student"], workflowDefinition, screenName);

        // Act
        var result = await controller.GetScreenData(workflowDefinition, screenName, _ct);

        //Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var screenDataDto = Assert.IsType<ScreenDataDto>(okResult.Value);
        Assert.Equal(screenName, screenDataDto.Name);
        Assert.Equal(workflowDefinition, screenDataDto.WorkflowDefinition);
        Assert.Equal(2, screenDataDto.Rows.Length);
        Assert.Null(screenDataDto.Groups);
    }

    [Fact]
    public async Task Screens_GetScreenData_GroupedScreen_ReturnsGroups()
    {
        // Arrange
        const string workflowDefinition = "Project";
        const string screenName = "Projects";

        var controller = BuildControllerWithRoles(["Student"], workflowDefinition, screenName);

        // Act
        var result = await controller.GetScreenData(workflowDefinition, screenName, _ct);

        //Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var screenDataDto = Assert.IsType<ScreenDataDto>(okResult.Value);
        Assert.Equal(screenName, screenDataDto.Name);
        Assert.Equal(workflowDefinition, screenDataDto.WorkflowDefinition);
        Assert.Empty(screenDataDto.Rows);

        var groups = screenDataDto.Groups;
        Assert.NotNull(groups);
        Assert.Equal(3, groups.Length);
        Assert.Contains(groups, g => g.Name == "approve-subject");
        Assert.Contains(groups, g => g.Name == "thesis-in-progress");
        Assert.Contains(groups, g => g.Name == "completed");
        // Both mocked instances are in the "Start" step, which maps to the approve-subject group
        Assert.Equal(2, groups.Single(g => g.Name == "approve-subject").Rows.Length);
    }

    private ScreensController BuildControllerWithRoles(
        string[] roles, string workflowDefinition, string screenName = "Projects")
    {
        MockCurrentUser(roles);
        MockEmptyRelatedInstanceLookups();

        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(workflowDefinition,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Dictionary<string, BsonValue>()
                {
                    { "CurrentStep", "Start" }
                },
                new Dictionary<string, BsonValue>()
                {
                    { "CurrentStep", "Start" }
                }
            ]);

        return new ScreensController(_screenDataService);
    }
}