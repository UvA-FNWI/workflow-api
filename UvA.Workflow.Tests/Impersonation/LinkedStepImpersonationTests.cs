using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Tests.WorkflowInstances;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Impersonation;

public class LinkedStepImpersonationTests
{
    [Fact]
    public async Task Start_UsesTheSelectedRoleForImportedActionsOnlyOnAuthorizedInstances()
    {
        var setup = new Setup();

        var response = await setup.Controller.StartImpersonation(setup.Core.Student.Id,
            new StartImpersonationDto("Supervisor"), default);
        var token = Assert.IsType<StartImpersonationResultDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        setup.Http.Request.Headers[RoleImpersonationConstants.HeaderName] = token.Token;

        Assert.Equal("Supervisor", await setup.Impersonation.GetImpersonatedRole(setup.Core.Student));
        Assert.Equal("Supervisor", await setup.Impersonation.GetImpersonatedRole(setup.Core.Source));
        Assert.True(await setup.Rights.Can(setup.Core.Source, RoleAction.Submit, "AssessJoint"));
        Assert.Null(await setup.Impersonation.GetImpersonatedRole(new WorkflowInstance { Id = "unrelated" }));
        setup.Users.Setup(service => service.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = "other", UserName = "other" });
        Assert.Null(await setup.Impersonation.GetImpersonatedRole(setup.Core.Source));
    }

    [Theory]
    [InlineData(false, true, StatusCodes.Status403Forbidden)]
    [InlineData(true, false, StatusCodes.Status400BadRequest)]
    public async Task Start_RequiresPermissionAndAValidRoleOnTheImportedInstance(
        bool canImpersonateSource, bool sourceHasRole, int expectedStatus)
    {
        var setup = new Setup(canImpersonateSource, sourceHasRole);

        var response = await setup.Controller.StartImpersonation(setup.Core.Student.Id,
            new StartImpersonationDto("Supervisor"), default);

        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(response.Result).StatusCode);
    }

    private sealed class Setup
    {
        public LinkedStepTestSetup Core { get; }
        public HttpContext Http { get; } = new DefaultHttpContext();
        public Mock<IUserService> Users { get; } = new();
        public RoleImpersonationService Impersonation { get; }
        public RightsService Rights { get; }
        public WorkflowInstancesController Controller { get; }

        public Setup(bool canImpersonateSource = true, bool sourceHasRole = true)
        {
            var files = LinkedStepConfigurationTests.Files();
            files["Common/Roles/Admin.yaml"] = "name: Admin";
            files["Common/Roles/Supervisor.yaml"] = "name: Supervisor";
            files["Individual/Actions.yaml"] = """
                                               globalActions:
                                                 - type: View
                                                   roles: [Admin, Supervisor]
                                                 - type: ImpersonateRoles
                                                   roles: [Admin]
                                               """;
            files["Shared/Actions.yaml"] = "globalActions:\n  - type: View\n    roles: [Admin]";
            if (canImpersonateSource)
                files["Shared/Actions.yaml"] += "\n  - type: ImpersonateRoles\n    roles: [Admin]";
            if (sourceHasRole)
                files["Shared/Actions.yaml"] += "\n  - type: View\n    roles: [Supervisor]";
            files["Shared/Steps/Assess.yaml"] +=
                "\nactions:\n  - type: Submit\n    roles: [Supervisor]\n    form: AssessJoint";
            files["Shared/Forms/AssessJoint.yaml"] = "name: AssessJoint\nstep: Assess";
            Core = new LinkedStepTestSetup(files);
            Core.Complete("Uploaded", 20);
            Core.Repository.Setup(repository => repository.GetById(Core.Student.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Core.Student);
            Users.Setup(service => service.GetCurrentUser(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new User { Id = "admin", UserName = "admin" });
            Users.Setup(service => service.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
                .ReturnsAsync(["Admin"]);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ImpersonationKey"] = ImpersonationTestHelpers.SigningKey }).Build();
            Impersonation = new RoleImpersonationService(configuration,
                new HttpContextAccessor { HttpContext = Http }, Users.Object);
            Rights = new RightsService(Core.Model, Users.Object, Core.Repository.Object, Core.Resolver, Impersonation);
            var instances = new InstanceService(Core.Repository.Object, Core.Model, Users.Object, Rights,
                null!, Core.Enricher, Core.Resolver);
            Controller = new WorkflowInstancesController(Users.Object, null!, Rights, null!, Core.Repository.Object,
                instances, null!, null!, Core.Model, Impersonation, null!, null!, null!);
        }
    }
}