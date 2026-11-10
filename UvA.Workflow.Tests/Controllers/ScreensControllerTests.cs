using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Tests.Controllers;

public class ScreensControllerTests : ControllerTestsBase
{
    private readonly ScreenDataService _screenDataService;

    public ScreensControllerTests() : base()
    {
        _screenDataService = new ScreenDataService(_modelService, _instanceService, _workflowInstanceRepoMock.Object,
            new InstanceAuthorizationFilterService(_rightsService, _modelService, _userServiceMock.Object,
                _workflowInstanceRepoMock.Object), _rightsService);
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
        Assert.Equal(workflowDefinition, screenDataDto.WorkflowDefinition.Name);
        Assert.Equal(2, screenDataDto.Rows.Length);
        Assert.Null(screenDataDto.Groups);
        Assert.Empty(screenDataDto.BulkActions!);
    }

    [Fact]
    public async Task Screens_GetScreenData_ReturnsConfiguredBulkButtonsInOrder()
    {
        var controller = BuildControllerWithRoles(["Coordinator"], "Project", "Projects");
        _modelService.WorkflowDefinitions["Project"].Screens.Single(s => s.Name == "Projects").BulkActions =
        [
            new BulkAction
            {
                Action = "Second", Label = new BilingualString("Second", "Tweede"),
                Icon = new Icon { Type = "checkmark-line" }
            },
            new BulkAction { Action = "First", Label = "First", Icon = new Icon { Type = "send-line" } }
        ];

        var result = await controller.GetScreenData("Project", "Projects", _ct);

        var dto = Assert.IsType<ScreenDataDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["Second", "First"], dto.BulkActions!.Select(action => action.Action));
        Assert.Equal("Tweede", dto.BulkActions![0].Label.Nl);
        Assert.Equal("checkmark-line", dto.BulkActions![0].Icon.Type);
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
        Assert.Equal(workflowDefinition, screenDataDto.WorkflowDefinition.Name);
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

    [Fact]
    public async Task Screens_GetScreenData_GroupsByComputedWorkflowStoppedBeforeStep()
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.StopWhen = new Condition { Value = new Value { Property = "EC", Equal = "42" } };
        var grouping = definition.Screens.Single(screen => screen.Name == "Projects").Grouping!;
        grouping.Groups =
        [
            .. grouping.Groups,
            new StepGroup
            {
                Name = "stopped",
                Title = "Stopped",
                Condition = new Condition
                {
                    Value = new Value { Property = ContextKeys.WorkflowStopped, Equal = "true" }
                }
            }
        ];
        var controller = BuildControllerWithRoles(["Student"], "Project", "Projects",
        [
            new Dictionary<string, BsonValue> { ["_id"] = "stopped", ["CurrentStep"] = "Start", ["EC"] = 42 },
            new Dictionary<string, BsonValue> { ["_id"] = "running", ["CurrentStep"] = "Start", ["EC"] = 30 }
        ]);

        var result = await controller.GetScreenData("Project", "Projects", _ct);

        var response = Assert.IsType<ScreenDataDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var groups = Assert.IsType<ScreenGroupDto[]>(response.Groups);
        Assert.Equal("stopped", Assert.Single(groups.Single(group => group.Name == "stopped").Rows).Id);
        Assert.Equal("running", Assert.Single(groups.Single(group => group.Name == "approve-subject").Rows).Id);
        Assert.Equal(2, groups.Sum(group => group.Rows.Length));
    }

    [Fact]
    public async Task Screens_GetScreenData_UsesConditionalProgressForActiveEvent()
    {
        var rejectedAt = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var controller = BuildControllerWithRoles(["Student"], "Project", "Projects",
        [
            new Dictionary<string, BsonValue>
            {
                ["CurrentStep"] = "Start",
                ["Events"] = new BsonDocument
                {
                    ["RejectSubject"] = new BsonDocument("Date", rejectedAt)
                }
            }
        ]);

        var result = await controller.GetScreenData("Project", "Projects", _ct);

        var response = Assert.IsType<ScreenDataDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var progressColumn = response.Columns.Single(column => column.IsCurrentStep);
        var row = Assert.Single(response.Groups!.Single(group => group.Name == "approve-subject").Rows);
        var progress = Assert.IsType<ProgressInformationDto>(row.Values[progressColumn.Id]);
        Assert.Equal("Revising proposal", progress.Text.En);
        Assert.Equal(StatusColor.Red, progress.Color);
    }

    [Fact]
    public async Task Screens_GetScreenData_UsesComputedWorkflowStoppedInProgress()
    {
        var definition = _modelService.WorkflowDefinitions["Project"];
        definition.StopWhen = new Condition { Value = new Value { Property = "EC", Equal = "42" } };
        definition.AllSteps.Single(step => step.Name == "Start").Progress.Add(new ProgressInformation
        {
            Condition = new Condition
            {
                Value = new Value { Property = ContextKeys.WorkflowStopped, Equal = "true" }
            },
            Text = "Paused",
            Color = StatusColor.Red
        });
        var controller = BuildControllerWithRoles(["Student"], "Project", "Projects",
        [
            new Dictionary<string, BsonValue> { ["CurrentStep"] = "Start", ["EC"] = 42 },
            new Dictionary<string, BsonValue> { ["CurrentStep"] = "Start", ["EC"] = 30 }
        ]);

        var result = await controller.GetScreenData("Project", "Projects", _ct);

        var response = Assert.IsType<ScreenDataDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var column = response.Columns.Single(column => column.IsCurrentStep);
        var groups = Assert.IsType<ScreenGroupDto[]>(response.Groups);
        Assert.Collection(groups.Single(group => group.Name == "approve-subject").Rows,
            row => Assert.Equal("Paused", Assert.IsType<ProgressInformationDto>(row.Values[column.Id]).Text.En),
            row => Assert.Equal("Writing proposal",
                Assert.IsType<ProgressInformationDto>(row.Values[column.Id]).Text.En));
        _workflowInstanceRepoMock.Verify(repository => repository.GetAllByType("Project",
            It.Is<Dictionary<string, string>>(projection =>
                projection["EC"] == "$Properties.EC" && !projection.ContainsKey(ContextKeys.WorkflowStopped)),
            It.IsAny<BsonDocument?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Screens_GetScreenData_RendersLastEventWithCreationDateFallback()
    {
        var createdAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var latest = createdAt.AddDays(6);
        _modelService.WorkflowDefinitions["Project"].AllSteps.Single(step => step.Name == "Start").Progress =
        [
            new ProgressInformation
            {
                Color = StatusColor.Green,
                Text = new BilingualString(
                    "Working ({{ dateShort(LastEvent) }}) on proposal",
                    "Werkt ({{ dateShort(LastEvent) }}) aan voorstel")
            }
        ];
        var controller = BuildControllerWithRoles(["Student"], "Project", "Projects",
        [
            new Dictionary<string, BsonValue>
            {
                ["CurrentStep"] = "Start",
                ["CreateDate"] = new BsonDateTime(createdAt),
                ["Events"] = new BsonDocument
                {
                    ["RejectSubject"] = new BsonDocument("Date", latest),
                    ["Start"] = new BsonDocument("Date", createdAt.AddDays(1))
                }
            },
            new Dictionary<string, BsonValue>
            {
                ["CurrentStep"] = "Start",
                ["CreateDate"] = new BsonDateTime(createdAt),
                ["Events"] = new BsonDocument()
            },
            new Dictionary<string, BsonValue>
            {
                ["CurrentStep"] = "Start",
                ["CreateDate"] = new BsonDateTime(createdAt),
                ["Events"] = BsonNull.Value
            },
            new Dictionary<string, BsonValue>
            {
                ["CurrentStep"] = "Start",
                ["CreateDate"] = new BsonDateTime(createdAt)
            }
        ]);

        var result = await controller.GetScreenData("Project", "Projects", _ct);

        var response = Assert.IsType<ScreenDataDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var column = response.Columns.Single(column => column.IsCurrentStep);
        var rows = response.Groups!.Single(group => group.Name == "approve-subject").Rows;
        var revised = Assert.IsType<ProgressInformationDto>(rows[0].Values[column.Id]);
        Assert.Equal("Working (07/09) on proposal", revised.Text.En);
        Assert.Equal("Werkt (07/09) aan voorstel", revised.Text.Nl);
        Assert.Equal(4, rows.Length);
        Assert.All(rows.Skip(1), row =>
        {
            var initial = Assert.IsType<ProgressInformationDto>(row.Values[column.Id]);
            Assert.Equal("Working (01/09) on proposal", initial.Text.En);
            Assert.Equal("Werkt (01/09) aan voorstel", initial.Text.Nl);
        });
        _workflowInstanceRepoMock.Verify(repository => repository.GetAllByType("Project",
            It.Is<Dictionary<string, string>>(projection =>
                projection["Events"] == "$Events" && projection["CreateDate"] == "$CreatedOn" &&
                !projection.ContainsKey("LastEvent")),
            It.IsAny<BsonDocument?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Screens_GetScreenData_IgnoresSuppressedProgressEvent()
    {
        var rejectedAt = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var controller = BuildControllerWithRoles(["Student"], "Project", "Projects",
        [
            new Dictionary<string, BsonValue>
            {
                ["CurrentStep"] = "Start",
                ["Events"] = new BsonDocument
                {
                    ["RejectSubject"] = new BsonDocument("Date", rejectedAt),
                    ["Start"] = new BsonDocument("Date", rejectedAt.AddDays(1))
                }
            }
        ]);

        var result = await controller.GetScreenData("Project", "Projects", _ct);

        var response = Assert.IsType<ScreenDataDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var progressColumn = response.Columns.Single(column => column.IsCurrentStep);
        var row = Assert.Single(response.Groups!.Single(group => group.Name == "approve-subject").Rows);
        var progress = Assert.IsType<ProgressInformationDto>(row.Values[progressColumn.Id]);
        Assert.Equal("Writing proposal", progress.Text.En);
        Assert.Equal(StatusColor.Green, progress.Color);
    }

    private ScreensController BuildControllerWithRoles(
        string[] roles,
        string workflowDefinition,
        string screenName = "Projects",
        List<Dictionary<string, BsonValue>>? rows = null)
    {
        MockCurrentUser(roles);
        MockEmptyRelatedInstanceLookups();

        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _workflowInstanceRepoMock.Setup(r => r.GetAllByType(workflowDefinition,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BsonDocument?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows ??
            [
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