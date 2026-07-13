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
using UvA.Workflow.Users;
using UvA.Workflow.Users.DataNose;

namespace UvA.Workflow.Tests.Impersonation;

public class UsersControllerImpersonationTests
{
    private const string Admin = "admin";
    private const string Target = "alice";

    private static UsersController BuildController(bool isSuperAdmin, bool targetExists)
    {
        var userService = new Mock<IUserService>();
        var adminUser = new User { Id = "admin-id", UserName = Admin };
        userService.Setup(s => s.GetUser(Admin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminUser);
        userService.Setup(s => s.GetRoles(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(isSuperAdmin
                ? new[] { DataNoseDirectoryKeys.SuperAdminRoleName }
                : new[] { "Student" });
        userService.Setup(s => s.GetUser(Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetExists ? new User { Id = "target-id", UserName = Target } : null);

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, Admin)], "TestAuth"))
        };
        var httpAccessor = new HttpContextAccessor { HttpContext = ctx };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImpersonationKey"] = ImpersonationTestHelpers.SigningKey
            })
            .Build();

        return new UsersController(
            userService.Object,
            null!,
            null!,
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

    [Fact]
    public async Task Impersonate_NonSuperAdmin_ReturnsForbidden()
    {
        var controller = BuildController(isSuperAdmin: false, targetExists: true);

        var result = await controller.Impersonate(new StartUserImpersonationDto(Target), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
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