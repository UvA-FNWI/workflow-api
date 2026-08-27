using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Events;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
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
        _eventRepoMock.Verify(r =>
                r.DeleteEvent(instance, It.Is<InstanceEvent>(e => e.Id == eventName), UnitTestsHelpers.AdminUser,
                    _ct),
            Times.Once);
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

    [Fact]
    public async Task Events_DeleteEvent_ReturnsUnauthorized_WhenNoCurrentUser()
    {
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"], "Start");
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await controller.DeleteEvent(instance.Id, "Start", _ct);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Events_DeleteEvent_ReturnsNotFound_WhenInstanceDoesNotExist()
    {
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"], "Start");
        _workflowInstanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowInstance?)null);

        var result = await controller.DeleteEvent(instance.Id, "Start", _ct);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);

        _eventRepoMock.Verify(r => r.DeleteEvent(It.IsAny<WorkflowInstance>(), It.IsAny<InstanceEvent>(),
            It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private (EventsController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string eventName)
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithEvents(b => b.WithId(eventName))
            .WithProperties(("Title", b => b.Value("My Thesis")))
            .Build();

        _eventRepoMock.Setup(r =>
            r.DeleteEvent(instance, It.IsAny<InstanceEvent>(), UnitTestsHelpers.AdminUser, _ct));

        MockInstance(instance);
        MockEmptyEventLog(instance);
        MockEmptyRelatedInstanceLookups();
        MockCurrentUser(roles);

        var controller =
            new EventsController(_workflowInstanceRepoMock.Object, _userServiceMock.Object, _rightsService,
                _eventService);

        return (controller, instance);
    }

    [Fact]
    public async Task Events_DeleteEvent_AttributesRealAdmin_WhenImpersonating()
    {
        const string eventName = "Start";

        // BuildControllerWithRoles calls MockCurrentUser internally; MockImpersonation
        // must come after to override GetCurrentUser/GetRealUser for the impersonation scenario.
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"], eventName);
        MockImpersonation("Coordinator");

        User? capturedUser = null;
        _eventRepoMock
            .Setup(r => r.DeleteEvent(
                It.IsAny<WorkflowInstance>(), It.IsAny<InstanceEvent>(),
                It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowInstance, InstanceEvent, User, CancellationToken>((_, _, user, _) => capturedUser = user)
            .Returns(Task.CompletedTask);

        var result = await controller.DeleteEvent(instance.Id, eventName, _ct);

        // The real admin (who is impersonating) must be attributed on the event,
        // not the impersonated target user.
        Assert.IsType<OkResult>(result);
        Assert.NotNull(capturedUser);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, capturedUser.Id);
        Assert.NotEqual(UnitTestsHelpers.ImpersonatedTarget.Id, capturedUser.Id);

        // Verify the service was asked for the *real* user, not just the current (impersonated) user.
        _userServiceMock.Verify(s => s.GetRealUser(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}