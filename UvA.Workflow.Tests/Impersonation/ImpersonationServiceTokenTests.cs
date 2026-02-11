using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Impersonation;

public class ImpersonationServiceTokenTests
{
    private static ImpersonationService CreateService(string? key = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImpersonationKey"] = key ?? ImpersonationTestHelpers.SigningKey
            })
            .Build();
        var userService = new Mock<IUserService>();
        userService.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = "id",
                UserName = "admin"
            });
        return new ImpersonationService(
            config,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            userService.Object,
            ImpersonationTestHelpers.CreateModelService());
    }

    [Fact]
    public async Task ValidateToken_ValidToken_ReturnsClaims()
    {
        var service = CreateService();

        var token = service.CreateToken("admin", "instance-123", "Student");
        var claims = await service.ValidateToken(token.Value);

        Assert.NotNull(claims);
        Assert.Equal("admin", claims.UserName);
        Assert.Equal("instance-123", claims.InstanceId);
        Assert.Equal("Student", claims.RoleName);
    }

    [Fact]
    public async Task ValidateToken_DifferentSigningKey_ReturnsNull()
    {
        var issuer = CreateService(ImpersonationTestHelpers.SigningKey);
        var validator = CreateService(ImpersonationTestHelpers.AlternateSigningKey);

        var token = issuer.CreateToken("admin", "instance-123", "Student");
        var claims = await validator.ValidateToken(token.Value);

        Assert.Null(claims);
    }

    [Fact]
    public async Task ValidateToken_ExpiredToken_ReturnsNull()
    {
        var service = CreateService();
        var expiredToken = CreateExpiredToken(ImpersonationTestHelpers.SigningKey);

        var claims = await service.ValidateToken(expiredToken);

        Assert.Null(claims);
    }

    private static string CreateExpiredToken(string key)
    {
        var claims = new Dictionary<string, object>
        {
            [ImpersonationConstants.TypeClaim] = ImpersonationConstants.TokenType,
            [ImpersonationConstants.UserClaim] = "admin",
            [ImpersonationConstants.InstanceClaim] = "instance-123",
            [ImpersonationConstants.RoleClaim] = "Student"
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = DateTime.UtcNow.AddMinutes(-5),
            Issuer = ImpersonationConstants.TokenIssuer,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(key)),
                SecurityAlgorithms.HmacSha512Signature),
            Claims = claims
        });
    }
}