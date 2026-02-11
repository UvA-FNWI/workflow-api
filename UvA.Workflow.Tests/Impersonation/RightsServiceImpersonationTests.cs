using MongoDB.Bson;
using Moq;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Impersonation;

public class RightsServiceImpersonationTests
{
    [Fact]
    public async Task Can_ViewAdminTools_UsesRealRoles_WhenNotImpersonating()
    {
        var modelService = ImpersonationTestHelpers.CreateModelService();
        var userService = new Mock<IUserService>();
        var repository = new Mock<IWorkflowInstanceRepository>();
        var impersonationContext = new Mock<IImpersonationContextService>();

        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Coordinator"]);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = ObjectId.GenerateNewId().ToString(),
                UserName = "admin"
            });
        impersonationContext
            .Setup(s => s.GetImpersonatedRole(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var rightsService = new RightsService(
            modelService,
            userService.Object,
            repository.Object,
            impersonationContext.Object);
        var instance = ImpersonationTestHelpers.CreateProjectInstance();

        var canViewAdminTools = await rightsService.Can(instance, RoleAction.ViewAdminTools);
        Assert.True(canViewAdminTools);
    }

    [Fact]
    public async Task Can_ViewAdminTools_UsesOnlyImpersonatedRole_WhenImpersonating()
    {
        var modelService = ImpersonationTestHelpers.CreateModelService();
        var userService = new Mock<IUserService>();
        var repository = new Mock<IWorkflowInstanceRepository>();
        var impersonationContext = new Mock<IImpersonationContextService>();

        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Coordinator"]);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = ObjectId.GenerateNewId().ToString(),
                UserName = "admin"
            });
        impersonationContext
            .Setup(s => s.GetImpersonatedRole(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Student");

        var rightsService = new RightsService(
            modelService,
            userService.Object,
            repository.Object,
            impersonationContext.Object);
        var instance = ImpersonationTestHelpers.CreateProjectInstance();

        var canViewAdminToolsImpersonated = await rightsService.Can(instance, RoleAction.ViewAdminTools);
        var canViewAdminToolsReal =
            await rightsService.Can(instance, RoleAction.ViewAdminTools, RightsEvaluationMode.RealUser);

        Assert.False(canViewAdminToolsImpersonated);
        Assert.True(canViewAdminToolsReal);
    }

    [Fact]
    public async Task GetAllowedActions_UnknownImpersonatedRole_ReturnsNoActions()
    {
        var modelService = ImpersonationTestHelpers.CreateModelService();
        var userService = new Mock<IUserService>();
        var repository = new Mock<IWorkflowInstanceRepository>();
        var impersonationContext = new Mock<IImpersonationContextService>();

        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Coordinator"]);
        impersonationContext
            .Setup(s => s.GetImpersonatedRole(It.IsAny<WorkflowInstance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("MissingRole");

        var rightsService = new RightsService(
            modelService,
            userService.Object,
            repository.Object,
            impersonationContext.Object);
        var instance = ImpersonationTestHelpers.CreateProjectInstance();

        var actions = await rightsService.GetAllowedActions(instance, RoleAction.ViewAdminTools);

        Assert.Empty(actions);
    }
}