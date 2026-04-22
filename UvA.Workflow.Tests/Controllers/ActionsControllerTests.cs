using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using UvA.Workflow.Api.Actions;
using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class ActionsControllerTests : ControllerTestsBase
{
    private readonly WorkflowInstanceDtoFactory _workflowInstanceDtoFactory;

    public ActionsControllerTests() : base()
    {
        var submissionDtoFactory =
            new SubmissionDtoFactory(new ArtifactTokenService(_configurationMock.Object), _modelService);
        _workflowInstanceDtoFactory =
            new WorkflowInstanceDtoFactory(_instanceService, _modelService,
                submissionDtoFactory, _workflowInstanceRepoMock.Object, _rightsService,
                new StepVersionService(_modelService, _eventRepoMock.Object), _workflowInstanceService,
                _loggerFactory.CreateLogger<WorkflowInstanceDtoFactory>());
    }

    [Theory]
    [InlineData("Coordinator", "ApproveCoordinator", "ApprovalCoordinator")]
    public async Task Actions_ExecuteAction_AllowedForUser(string role, string actionName, string stepName)
    {
        // Arrange
        var (controller, instance) = BuildControllerWithRoles([role], stepName);
        var input = new ExecuteActionInputDto(ActionType.Execute, instance.Id, actionName);

        // Act
        var result = await controller.ExecuteAction(input, _ct);

        //Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("Student", "ApproveCoordinator", "ApprovalCoordinator")]
    public async Task Actions_ExecuteAction_ForbiddenForUser(string role, string actionName, string stepName)
    {
        // Arrange
        var (controller, instance) = BuildControllerWithRoles([role], stepName);
        var input = new ExecuteActionInputDto(ActionType.Execute, instance.Id, actionName);

        // Act
        var result = await controller.ExecuteAction(input, _ct);

        //Assert
        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, objectResult.StatusCode);
    }

    [Fact]
    public async Task Actions_ExecuteAction_ReturnsUnauthorized_WhenNoCurrentUser()
    {
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"], "ApprovalCoordinator");
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var input = new ExecuteActionInputDto(ActionType.Execute, instance.Id, "ApproveCoordinator");

        var result = await controller.ExecuteAction(input, _ct);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Actions_ExecuteAction_ReturnsNotFound_WhenInstanceDoesNotExist()
    {
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"], "ApprovalCoordinator");

        _workflowInstanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        var input = new ExecuteActionInputDto(ActionType.Execute, instance.Id, "ApproveCoordinator");

        var result = await controller.ExecuteAction(input, _ct);

        var badRequest = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, badRequest.StatusCode);
    }

    [Fact]
    public async Task Actions_ExecuteAction_ReturnsBadRequest_WhenActionNameMissing()
    {
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"], "ApprovalCoordinator");
        var input = new ExecuteActionInputDto(ActionType.Execute, instance.Id, null);

        var result = await controller.ExecuteAction(input, _ct);

        var badRequest = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    private (ActionsController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string stepName = "Start", string workflowDefinition = "Project")
    {
        var contextInstance = new WorkflowInstanceBuilder()
            .With("Context", stepName)
            .WithProperties(
                ("Name", _ => "CourseTitle"),
                ("ExternalId", _ => "DummyId"),
                ("Type", _ => "Course"),
                ("Coordinator", _ => ""))
            .Build();

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition, stepName)
            .WithProperties(("Course", _ => contextInstance.Id))
            .Build();

        MockInstance(instance);
        MockEmptyEventLog(instance);
        MockEmptyRelatedInstanceLookups();
        MockInstance(contextInstance);
        MockCurrentUser(roles);

        var controller =
            new ActionsController(_workflowInstanceRepoMock.Object, _userServiceMock.Object, _rightsService,
                _effectService, _jobService, _workflowInstanceDtoFactory, _instanceService);

        return (controller, instance);
    }
}