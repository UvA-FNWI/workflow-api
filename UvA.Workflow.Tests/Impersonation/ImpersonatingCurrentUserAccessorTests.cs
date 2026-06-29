using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using UvA.Workflow.Api.Authentication;

namespace UvA.Workflow.Tests.Impersonation;

public class ImpersonatingCurrentUserAccessorTests
{
    private static UserImpersonationTokenService CreateTokenService(IHttpContextAccessor httpAccessor)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImpersonationKey"] = ImpersonationTestHelpers.SigningKey
            })
            .Build();
        return new UserImpersonationTokenService(config, httpAccessor);
    }

    private static ImpersonatingCurrentUserAccessor Build(string? realUser, string? headerToken)
    {
        var ctx = new DefaultHttpContext();
        if (realUser != null)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, realUser)], "TestAuth"));
        if (headerToken != null)
            ctx.Request.Headers[UserImpersonationConstants.HeaderName] = headerToken;

        var httpAccessor = new HttpContextAccessor { HttpContext = ctx };

        return new ImpersonatingCurrentUserAccessor(
            new HttpContextCurrentUserAccessor(httpAccessor),
            CreateTokenService(httpAccessor));
    }

    private static string TokenFor(string realUser, string target)
        => CreateTokenService(new HttpContextAccessor { HttpContext = new DefaultHttpContext() })
            .CreateToken(realUser, target).Value;

    [Fact]
    public void NoToken_ReturnsRealUser()
    {
        Assert.Equal("admin", Build("admin", null).GetCurrentUserName());
    }

    [Fact]
    public void ValidToken_ReturnsTarget()
    {
        var token = TokenFor("admin", "alice");

        Assert.Equal("alice", Build("admin", token).GetCurrentUserName());
    }

    [Fact]
    public void TokenIssuedForDifferentAdmin_ReturnsRealUser()
    {
        var token = TokenFor("admin", "alice");

        // A non-admin presenting an admin's token gets no override.
        Assert.Equal("mallory", Build("mallory", token).GetCurrentUserName());
    }

    [Fact]
    public void Unauthenticated_ReturnsNull()
    {
        var token = TokenFor("admin", "alice");

        Assert.Null(Build(null, token).GetCurrentUserName());
    }
}