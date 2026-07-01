using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using UvA.Workflow.Api.Authentication;

namespace UvA.Workflow.Tests.Impersonation;

public class UserImpersonationTokenServiceTests
{
    private static UserImpersonationTokenService CreateService(HttpContext httpContext, string? key = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImpersonationKey"] = key ?? ImpersonationTestHelpers.SigningKey
            })
            .Build();
        return new UserImpersonationTokenService(config, new HttpContextAccessor { HttpContext = httpContext });
    }

    private static HttpContext WithHeader(string? token)
    {
        var ctx = new DefaultHttpContext();
        if (token != null)
            ctx.Request.Headers[UserImpersonationConstants.HeaderName] = token;
        return ctx;
    }

    [Fact]
    public void TryResolveTargetUser_ValidToken_ReturnsTarget()
    {
        var token = CreateService(new DefaultHttpContext()).CreateToken("admin", "alice").Value;

        var service = CreateService(WithHeader(token));

        Assert.Equal("alice", service.TryResolveTargetUser("admin"));
    }

    [Fact]
    public void TryResolveTargetUser_NoHeader_ReturnsNull()
    {
        var service = CreateService(WithHeader(null));

        Assert.Null(service.TryResolveTargetUser("admin"));
    }

    [Fact]
    public void TryResolveTargetUser_DifferentRealUser_ReturnsNull()
    {
        var token = CreateService(new DefaultHttpContext()).CreateToken("admin", "alice").Value;

        // The same token presented under a different (non-admin) session must be ignored.
        var service = CreateService(WithHeader(token));

        Assert.Null(service.TryResolveTargetUser("mallory"));
    }

    [Fact]
    public void TryResolveTargetUser_DifferentSigningKey_ReturnsNull()
    {
        var token = CreateService(new DefaultHttpContext(), ImpersonationTestHelpers.SigningKey)
            .CreateToken("admin", "alice").Value;

        var service = CreateService(WithHeader(token), ImpersonationTestHelpers.AlternateSigningKey);

        Assert.Null(service.TryResolveTargetUser("admin"));
    }

    [Fact]
    public void TryResolveTargetUser_ExpiredToken_ReturnsNull()
    {
        var token = CreateRawToken(DateTime.UtcNow.AddMinutes(-5),
            ImpersonationTokenCodec.Issuer, UserImpersonationConstants.TokenType);

        var service = CreateService(WithHeader(token));

        Assert.Null(service.TryResolveTargetUser("admin"));
    }

    [Fact]
    public void TryResolveTargetUser_WrongIssuer_ReturnsNull()
    {
        var token = CreateRawToken(DateTime.UtcNow.AddHours(1),
            "not-workflow", UserImpersonationConstants.TokenType);

        var service = CreateService(WithHeader(token));

        Assert.Null(service.TryResolveTargetUser("admin"));
    }

    [Fact]
    public void TryResolveTargetUser_WrongTokenType_ReturnsNull()
    {
        var token = CreateRawToken(DateTime.UtcNow.AddHours(1),
            ImpersonationTokenCodec.Issuer, "something-else");

        var service = CreateService(WithHeader(token));

        Assert.Null(service.TryResolveTargetUser("admin"));
    }

    private static string CreateRawToken(DateTime expires, string issuer, string type)
    {
        var claims = new Dictionary<string, object>
        {
            [ImpersonationTokenCodec.TypeClaim] = type,
            [UserImpersonationConstants.RealUserClaim] = "admin",
            [UserImpersonationConstants.TargetUserClaim] = "alice"
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
        {
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = expires,
            Issuer = issuer,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(ImpersonationTestHelpers.SigningKey)),
                SecurityAlgorithms.HmacSha512Signature),
            Claims = claims
        });
    }
}