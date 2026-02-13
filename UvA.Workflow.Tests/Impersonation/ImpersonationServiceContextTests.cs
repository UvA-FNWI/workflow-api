using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Entities.Domain;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Impersonation;

public class ImpersonationServiceContextTests
{
    private static ImpersonationService CreateService(
        HttpContextAccessor httpContextAccessor,
        IUserService userService)
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

    private static ImpersonationService CreateContextService(
        string token,
        string currentUserName)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        httpContextAccessor.HttpContext!.Request.Headers[ImpersonationConstants.HeaderName] = token;

        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                UserName = currentUserName
            });

        return CreateService(httpContextAccessor, userService.Object);
    }

    private static string CreateToken(string userName, string instanceId, string roleName)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                UserName = userName
            });
        return CreateService(httpContextAccessor, userService.Object).CreateToken(userName, instanceId, roleName).Value;
    }

    [Fact]
    public async Task GetImpersonatedRole_ValidToken_ReturnsRole()
    {
        var token = CreateToken("admin", "instance-1", "Student");
        var contextService = CreateContextService(token, "admin");

        var role = await contextService.GetImpersonatedRole(
            ImpersonationTestHelpers.CreateProjectInstance("instance-1"));

        Assert.Equal("Student", role);
    }

    [Fact]
    public async Task GetImpersonatedRole_WrongUser_ReturnsNull()
    {
        var token = CreateToken("admin", "instance-1", "Student");
        var contextService = CreateContextService(token, "other-admin");

        var role = await contextService.GetImpersonatedRole(
            ImpersonationTestHelpers.CreateProjectInstance("instance-1"));

        Assert.Null(role);
    }

    [Fact]
    public async Task GetImpersonatedRole_WrongInstance_ReturnsNull()
    {
        var token = CreateToken("admin", "instance-1", "Student");
        var contextService = CreateContextService(token, "admin");

        var role = await contextService.GetImpersonatedRole(
            ImpersonationTestHelpers.CreateProjectInstance("instance-2"));

        Assert.Null(role);
    }

    [Fact]
    public async Task GetImpersonatedRole_IrrelevantRole_ReturnsRole()
    {
        var token = CreateToken("admin", "instance-1", "NonExistingRole");
        var contextService = CreateContextService(token, "admin");

        var role = await contextService.GetImpersonatedRole(
            ImpersonationTestHelpers.CreateProjectInstance("instance-1"));

        Assert.Equal("NonExistingRole", role);
    }
}