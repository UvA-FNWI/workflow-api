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
    public async Task Screens_GetScreenData_ReturnsData()
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
        Assert.Equal(2, screenDataDto.Rows.Length);
    }

    [Fact]
    public async Task Screens_GetGroupedScreenData_ReturnsGroupedData()
    {
        // Arrange
        const string workflowDefinition = "Project";
        const string screenName = "Projects";

        var controller = BuildControllerWithRoles(["Student"], workflowDefinition, screenName);
        // Act
        var result = await controller.GetGroupedScreenData(screenName, workflowDefinition, _ct);

        //Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var screenDataDto = Assert.IsType<GroupedScreenDataDto>(okResult.Value);
        Assert.Equal(screenName, screenDataDto.Name);
        Assert.Equal(3, screenDataDto.Groups.Length);
        Assert.Contains(screenDataDto.Groups, g => g.Name == "assign-subject");
        Assert.Contains(screenDataDto.Groups, g => g.Name == "thesis-in-progress");
        Assert.Contains(screenDataDto.Groups, g => g.Name == "completed");
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