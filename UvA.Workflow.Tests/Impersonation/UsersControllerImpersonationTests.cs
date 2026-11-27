using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.Impersonation;

public class UsersControllerImpersonationTests
{
    private const string Admin = "admin";
    private const string Target = "alice";

    private static UsersController BuildController(bool isSuperAdmin, bool targetExists, string? deployedAction = null,
        string? impersonatedRole = null)
    {
        var userService = new Mock<IUserService>();
        var adminUser = new User { Id = "admin-id", UserName = Admin };
        string[] roles = isSuperAdmin ? ["SystemAdmin"] : ["Support"];
        var effectiveUser = impersonatedRole == null ? adminUser : new User { UserName = "impersonated" };
        string[] effectiveRoles = impersonatedRole == null ? roles : [impersonatedRole];
        userService.Setup(s => s.GetRealUser(It.IsAny<CancellationToken>())).ReturnsAsync(adminUser);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(effectiveUser);
        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>())).ReturnsAsync(effectiveRoles);
        userService.Setup(s => s.GetUser(Admin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminUser);
        userService.Setup(s => s.GetRoles(adminUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        if (impersonatedRole != null)
            userService.Setup(s => s.GetRoles(effectiveUser, It.IsAny<CancellationToken>()))
                .ReturnsAsync(effectiveRoles);
        userService.Setup(s => s.GetUser(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetExists ? new User { Id = "target-id", UserName = Target } : null);

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, Admin)], "TestAuth"))
        };
        var httpAccessor = new HttpContextAccessor { HttpContext = ctx };
        var resolver = new ModelServiceResolver(httpAccessor);
        var baselineFiles = deployedAction is null
            ? new Dictionary<string, string>
            {
                ["Common/Roles/SystemAdmin.yaml"] =
                    "name: SystemAdmin\nactions:\n  - type: AccessGlobalAdminTools\n  - type: ImpersonateUsers\n"
            }
            : new Dictionary<string, string>
            {
                ["Common/Roles/Deployed.yaml"] = $"name: Deployed\nactions:\n  - type: {deployedAction}\n",
                ["Common/Roles/Support.yaml"] = "name: Support\ninheritFrom: [Deployed]\n",
                ["Common/Roles/Developer.yaml"] = "name: Developer\nactions:\n  - type: AccessGlobalAdminTools\n"
            };
        resolver.AddOrUpdate("", new ModelParser(new DictionaryProvider(baselineFiles)), "",
            kind: VersionKind.Baseline);
        if (deployedAction != null)
        {
            var previewFiles = new Dictionary<string, string>
            {
                ["Common/Roles/Support.yaml"] =
                    "name: Support\nactions:\n  - type: AccessGlobalAdminTools\n  - type: ImpersonateUsers\n"
            };
            resolver.AddOrUpdate("preview", new ModelParser(new DictionaryProvider(previewFiles)), "");
            ctx.Request.Headers[ModelServiceResolver.VersionHeader] = "preview";
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImpersonationKey"] = ImpersonationTestHelpers.SigningKey
            })
            .Build();

        var model = resolver.Resolve().ModelService;
        var repository = Mock.Of<IWorkflowInstanceRepository>();
        var rightsService = new RightsService(model, userService.Object, repository,
            UnitTestsHelpers.CreateStateResolver(model, repository));
        return new UsersController(
            userService.Object,
            null!,
            repository,
            rightsService,
            null!,
            null!,
            new HttpContextCurrentUserAccessor(httpAccessor),
            new UserImpersonationTokenService(config, httpAccessor),
            null!,
            Mock.Of<ILogger<UsersController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };
    }

    [Theory]
    [InlineData("AccessGlobalAdminTools", false, true, false)]
    [InlineData("ImpersonateUsers", false, false, true)]
    [InlineData("ViewAdminTools", false, false, false)]
    [InlineData("ViewAdminTools", true, false, false)]
    public async Task AdminActions_AreIndependentAndComeFromDeployedRoles(
        string deployedAction, bool isSuperAdmin, bool canAccessGlobalAdminTools, bool canImpersonateUsers)
    {
        var controller = BuildController(isSuperAdmin, targetExists: true, deployedAction);

        var me = await controller.GetLoggedInUser(CancellationToken.None);
        var user = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(me.Result).Value);
        Assert.Equal(canAccessGlobalAdminTools, user.IsSuperAdmin);
        Assert.Equal(canImpersonateUsers, user.CanImpersonateUsers);

        var result = await controller.Impersonate(new StartUserImpersonationDto(Target), CancellationToken.None);
        if (canImpersonateUsers)
            Assert.IsType<OkObjectResult>(result.Result);
        else
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task GetLoggedInUser_DevelopRequiresTheRealUsersGrant()
    {
        var controller = BuildController(isSuperAdmin: false, targetExists: true,
            deployedAction: "ImpersonateUsers", impersonatedRole: "Developer");

        var result = await controller.GetLoggedInUser(CancellationToken.None);

        var user = Assert.IsType<UserDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(user.IsSuperAdmin);
    }

    [Fact]
    public async Task Impersonate_NonSuperAdmin_ReturnsForbidden()
    {
        var controller = BuildController(isSuperAdmin: false, targetExists: true);

        var result = await controller.Impersonate(new StartUserImpersonationDto(Target), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task Impersonate_DoesNotUseTheEffectiveUsersImpersonationGrant()
    {
        var controller = BuildController(isSuperAdmin: false, targetExists: true, impersonatedRole: "SystemAdmin");

        var result = await controller.Impersonate(new StartUserImpersonationDto(Target), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task Impersonate_UnknownTarget_ReturnsNotFound()
    {
        var controller = BuildController(isSuperAdmin: true, targetExists: false);

        var result = await controller.Impersonate(new StartUserImpersonationDto(Target), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("ImpersonationTargetNotFound", error.ErrorCode);
    }

    [Fact]
    public async Task Impersonate_SuperAdmin_ReturnsToken()
    {
        var controller = BuildController(isSuperAdmin: true, targetExists: true);

        var result = await controller.Impersonate(new StartUserImpersonationDto(Target), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<UserImpersonationStartedDto>(okResult.Value);
        Assert.False(string.IsNullOrWhiteSpace(payload.Token));
        Assert.True(payload.ExpiresAtUtc > DateTime.UtcNow);
    }
}