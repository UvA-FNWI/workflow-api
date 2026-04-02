using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Events;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class EventsControllerTests : ControllerTestsBase
{
    [Theory]
    [InlineData("Coordinator", "Start")]
    public async Task Events_DeleteEvent_AllowWithAdminRights(string role, string eventName)
    {
        // Arrange
        var (controller, instance) = BuildControllerWithRoles([role], eventName);
        // Act
        var result = await controller.DeleteEvent(instance.Id, eventName, _ct);
        //Assert
        Assert.IsType<OkResult>(result);
    }

    [Theory]
    [InlineData("Student", "Start")]
    public async Task Events_DeleteEvent_ThrowsForbiddenException(string role, string eventName)
    {
        // Arrange
        var (controller, instance) = BuildControllerWithRoles([role], eventName);
        // Act and Assert
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            controller.DeleteEvent(instance.Id, eventName, _ct));
    }

    private (EventsController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string eventName)
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithEvents(b => b.WithId(eventName))
            .WithProperties(("Title", b => b.Value("My Thesis")))
            .Build();

        _eventRepoMock.Setup(r => r.GetEventLogEntriesForInstance(instance.Id,
                It.IsAny<List<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _eventRepoMock.Setup(r =>
            r.DeleteEvent(instance, It.IsAny<InstanceEvent>(), ControllerTestsHelpers.AdminUser, _ct));

        _workflowInstanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _workflowInstanceRepoMock.Setup(r => r.GetAllById(It.IsAny<string[]>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _userServiceMock.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ControllerTestsHelpers.AdminUser);

        var controller =
            new EventsController(_workflowInstanceRepoMock.Object, _userServiceMock.Object, _rightsService,
                _eventService);

        return (controller, instance);
    }
}