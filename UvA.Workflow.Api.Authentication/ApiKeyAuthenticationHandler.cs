using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;

namespace UvA.Workflow.Api.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Section = "Authentication";
    public string? ApiKey { get; set; }
}

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string AuthenticationScheme = "ApiKey";
    private const string HeaderName = "Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
            return AuthenticateResult.NoResult();

        var providedKey = values.ToString().Trim();

        if (string.IsNullOrEmpty(providedKey))
            return AuthenticateResult.Fail("Missing API key");

        providedKey = providedKey.Trim();

        if (!string.Equals(providedKey, Options.ApiKey, StringComparison.Ordinal))
        {
            await Task.Delay(200);
            return AuthenticateResult.Fail("Invalid API key");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, UserServiceBase.ApiUserName),
            new("auth_scheme", AuthenticationScheme)
        };
        var identity = new ClaimsIdentity(claims, AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), AuthenticationScheme);

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers["WWW-Authenticate"] = AuthenticationScheme;
        return Task.CompletedTask;
    }
}

public static class ApiKeyAuthenticationHandlerExtensions
{
    public static AuthenticationBuilder AddApiKeyAuthentication(this AuthenticationBuilder builder,
        Action<ApiKeyAuthenticationOptions> options)
    {
        return builder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationHandler.AuthenticationScheme,
            null,
            options
        );
    }
}