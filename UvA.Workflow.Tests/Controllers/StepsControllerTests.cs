using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Steps;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class StepsControllerTests : ControllerTestsBase
{
    private readonly IStepVersionService _stepVersionService;

    public StepsControllerTests() : base()
    {
        _stepVersionService = new StepVersionService(_modelService, _eventRepoMock.Object);
    }

    [Theory]
    [InlineData("Student")]
    public async Task Steps_GetStepVersions_AllowWithViewRights(string role)
    {
        // Arrange
        const string stepName = "Assessment";
        var (controller, instance) = BuildControllerWithRoles([role], stepName);
        // Act
        var result = await controller.GetStepVersions(instance.Id, stepName, _ct);
        //Assert
        Assert.IsType<ActionResult<List<StepVersion>>>(result);
    }

    [Theory]
    [InlineData("HasNoRights")]
    public async Task Steps_GetStepVersions_DenyWithoutViewRights(string role)
    {
        // Arrange
        const string stepName = "Assessment";
        var (controller, instance) = BuildControllerWithRoles([role], stepName);
        // Act and Assert
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            controller.GetStepVersions(instance.Id, stepName, _ct));
    }

    [Fact]
    public async Task Steps_GetStepVersions_ReturnsUnauthorized_WhenNoCurrentUser()
    {
        const string stepName = "Assessment";
        var (controller, instance) = BuildControllerWithRoles(["Student"], stepName);
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await controller.GetStepVersions(instance.Id, stepName, _ct);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Steps_GetStepVersions_ReturnsNotFound_WhenInstanceDoesNotExist()
    {
        const string stepName = "Assessment";
        var (controller, instance) = BuildControllerWithRoles(["Student"], stepName);
        _workflowInstanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        var result = await controller.GetStepVersions(instance.Id, stepName, _ct);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Steps_GetStepVersions_ReturnsNotFoundWithMessage_WhenStepDoesNotExist()
    {
        const string stepName = "UnknownStep";
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Assessment")
            .Build();

        MockInstance(instance);
        MockCurrentUser("Student");

        var stepVersionServiceMock = new Mock<IStepVersionService>();
        stepVersionServiceMock.Setup(s => s.GetStepVersions(instance, stepName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntityNotFoundException("Step", stepName));

        var controller = new StepsController(
            _userServiceMock.Object,
            _rightsService,
            _workflowInstanceRepoMock.Object,
            stepVersionServiceMock.Object);

        var result = await controller.GetStepVersions(instance.Id, stepName, _ct);

        var objectResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.NotNull(objectResult.Value);
        var messageProperty = objectResult.Value.GetType().GetProperty("message");
        Assert.NotNull(messageProperty);
        Assert.Equal($"Entity Step {stepName} not found", messageProperty.GetValue(objectResult.Value));
    }

    private (StepsController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string stepName)
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: stepName)
            .WithEvents(b => b.WithId(stepName))
            .Build();

        MockEmptyEventLog(instance);
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockCurrentUser(roles);

        var controller = new StepsController(_userServiceMock.Object, _rightsService, _workflowInstanceRepoMock.Object,
            _stepVersionService);

        return (controller, instance);
    }
}