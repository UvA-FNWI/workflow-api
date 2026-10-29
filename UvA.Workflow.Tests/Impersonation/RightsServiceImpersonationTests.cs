using MongoDB.Bson;
using Moq;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

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
            await rightsService.Can(instance, [RoleAction.ViewAdminTools], RightsEvaluationMode.RealUser);

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

    [Theory]
    [InlineData("Coordinator", "Course")]
    [InlineData("LocalAdmin", "Faculty")]
    public async Task Impersonation_IncludesRoleInheritedFromReferences(
        string roleName,
        string assignmentDefinition)
    {
        var modelService = ImpersonationTestHelpers.CreateModelService();
        var userService = new Mock<IUserService>();
        var repository = new Mock<IWorkflowInstanceRepository>();
        var impersonationContext = new Mock<IImpersonationContextService>();

        var userId = ObjectId.GenerateNewId();
        var faculty = new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkflowDefinition = "Faculty",
            Properties = new Dictionary<string, BsonValue>()
        };
        var programme = new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkflowDefinition = "Programme",
            Properties = new Dictionary<string, BsonValue>
            {
                ["Faculty"] = faculty.Id
            }
        };
        var course = new WorkflowInstance
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkflowDefinition = "Course",
            Properties = new Dictionary<string, BsonValue>
            {
                ["Programme"] = programme.Id
            }
        };
        var references = new[] { course, programme, faculty };
        references.Single(i => i.WorkflowDefinition == assignmentDefinition)
            .Properties[roleName] = new BsonArray
        {
            new BsonDocument
            {
                ["_id"] = userId,
                ["UserName"] = "testuser"
            }
        };

        var instance = ImpersonationTestHelpers.CreateProjectInstance();
        instance.Properties["Course"] = course.Id;

        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId.ToString(),
                UserName = "testuser"
            });
        repository.Setup(r => r.GetByIds(
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string[] ids, CancellationToken _) =>
                references.Where(i => ids.Contains(i.Id)).ToArray());
        impersonationContext
            .Setup(s => s.GetImpersonatedRole(instance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roleName);

        var rightsService = new RightsService(
            modelService, userService.Object, repository.Object, impersonationContext.Object);

        // The real user's assignment is resolved exclusively through references.
        Assert.Contains(roleName, await new RightsService(
            modelService, userService.Object, repository.Object).GetViewerRoles(instance));
        Assert.True(await rightsService.Can(
            instance, [RoleAction.View], RightsEvaluationMode.RealUser));

        Assert.Contains(
            rightsService.GetImpersonationTargetRoles(instance),
            role => role.Name == roleName);
        Assert.Equal(new[] { roleName }, await rightsService.GetViewerRoles(instance));
        Assert.True(await rightsService.Can(instance, RoleAction.View));
    }

    [Fact]
    public async Task Impersonation_IncludesRolePropertyInheritedFromParentDefinition()
    {
        var modelService = ImpersonationTestHelpers.CreateModelService();
        var userService = new Mock<IUserService>();
        var repository = new Mock<IWorkflowInstanceRepository>();
        var impersonationContext = new Mock<IImpersonationContextService>();
        var instance = ImpersonationTestHelpers.CreateProjectInstance();

        // Supervisor is declared on Project-Base, not directly on Project.
        Assert.Equal("Project-Base",
            modelService.WorkflowDefinitions["Project"].InheritsFrom);
        Assert.Contains(
            modelService.WorkflowDefinitions["Project-Base"].Properties,
            property => property.Name == "Supervisor");
        Assert.Contains(
            modelService.WorkflowDefinitions["Project"].Properties,
            property => property.Name == "Supervisor");

        impersonationContext
            .Setup(s => s.GetImpersonatedRole(instance, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Supervisor");

        var rightsService = new RightsService(
            modelService, userService.Object, repository.Object, impersonationContext.Object);

        Assert.Contains(
            rightsService.GetImpersonationTargetRoles(instance),
            role => role.Name == "Supervisor");
        Assert.Equal(new[] { "Supervisor" }, await rightsService.GetViewerRoles(instance));
        Assert.True(await rightsService.Can(instance, RoleAction.View));
    }
}