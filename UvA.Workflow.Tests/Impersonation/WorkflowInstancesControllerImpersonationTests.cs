using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Impersonation;

public class WorkflowInstancesControllerImpersonationTests
{
    private static ImpersonationService CreateImpersonationService(
        IUserService userService,
        IHttpContextAccessor httpContextAccessor)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImpersonationKey"] = ImpersonationTestHelpers.SigningKey
            })
            .Build();
        return new ImpersonationService(
            config,
            httpContextAccessor,
            userService);
    }

    private static string CreateToken(string userName, string instanceId, string roleName)
    {
        var userService = new Mock<IUserService>();
        var modelService = ImpersonationTestHelpers.CreateModelService();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var service = CreateImpersonationService(userService.Object, accessor);
        return service.CreateToken(userName, instanceId, roleName).Value;
    }

    private static WorkflowInstancesController CreateController(
        IUserService userService,
        RightsService rightsService,
        IWorkflowInstanceRepository repository,
        ModelService modelService,
        ImpersonationService impersonationService,
        HttpContext httpContext)
    {
        var controller = new WorkflowInstancesController(
            userService,
            null!,
            rightsService,
            null!,
            repository,
            null!,
            null!,
            modelService,
            impersonationService
        );

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    private static (WorkflowInstancesController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles,
        string? headerToken = null)
    {
        var modelService = ImpersonationTestHelpers.CreateModelService();
        var repository = new Mock<IWorkflowInstanceRepository>();
        var userService = new Mock<IUserService>();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        var instance = ImpersonationTestHelpers.CreateProjectInstance();
        repository.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        userService.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                UserName = "admin"
            });

        var rightsService = new RightsService(modelService, userService.Object, repository.Object);
        var impersonationService = CreateImpersonationService(userService.Object, accessor);
        var controller = CreateController(userService.Object, rightsService, repository.Object, modelService,
            impersonationService, accessor.HttpContext!);

        if (!string.IsNullOrWhiteSpace(headerToken))
            controller.HttpContext.Request.Headers[ImpersonationConstants.HeaderName] = headerToken;

        return (controller, instance);
    }

    [Fact]
    public async Task GetImpersonationRoles_WithoutAdminTools_ReturnsForbidden()
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"]);

        var result = await controller.GetImpersonationRoles(instance.Id, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetImpersonationRoles_WithAdminTools_ReturnsRoles()
    {
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"]);

        var result = await controller.GetImpersonationRoles(instance.Id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var roles = Assert.IsAssignableFrom<IEnumerable<ImpersonationRoleDto>>(okResult.Value);
        Assert.Contains(roles, r => r.Name == "Student");
        Assert.Contains(roles, r => r.Name == "Coordinator");
    }

    [Fact]
    public async Task StartImpersonation_InvalidRole_ReturnsBadRequest()
    {
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"]);

        var result = await controller.StartImpersonation(
            instance.Id,
            new StartImpersonationDto("NotARole"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("InvalidImpersonationRole", error.ErrorCode);
    }

    [Fact]
    public async Task StartImpersonation_ValidRole_ReturnsToken()
    {
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"]);

        var result = await controller.StartImpersonation(
            instance.Id,
            new StartImpersonationDto("student"),
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<StartImpersonationResultDto>(okResult.Value);
        Assert.Equal(instance.Id, payload.InstanceId);
        Assert.Equal("Student", payload.Role.Name);
        Assert.False(string.IsNullOrWhiteSpace(payload.Token));
        Assert.True(payload.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task StartImpersonation_WithoutAdminTools_ReturnsForbidden_EvenWithHeader()
    {
        var token = CreateToken("admin", "other-instance", "Student");
        var (controller, instance) = BuildControllerWithRoles(["Student"], token);

        var result = await controller.StartImpersonation(
            instance.Id,
            new StartImpersonationDto("Student"),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }
}