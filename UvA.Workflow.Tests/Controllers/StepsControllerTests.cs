using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Steps;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;
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

    private (StepsController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string stepName)
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: stepName)
            .WithEvents(b => b.WithId(stepName))
            .Build();

        _eventRepoMock.Setup(r => r.GetEventLogEntriesForInstance(instance.Id,
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _instanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _userServiceMock.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ControllerTestsHelpers.AdminUser);

        var controller = new StepsController(_userServiceMock.Object, _rightsService, _instanceRepoMock.Object,
            _stepVersionService);

        return (controller, instance);
    }
}