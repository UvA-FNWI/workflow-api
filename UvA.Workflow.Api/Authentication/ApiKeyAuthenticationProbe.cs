using Microsoft.AspNetCore.Http;
using UvA.Workflow.Api.Authentication.Abstractions;

namespace UvA.Workflow.Api.Authentication;

public class ApiKeyAuthenticationProbe : IAuthenticationSchemeProbe
{
    public string SchemeName => ApiKeyAuthenticationHandler.AuthenticationScheme;
    public int Order => 1000;

    public bool CanHandle(HttpContext context)
        => context.Request.Headers.TryGetValue("Api-Key", out var values) &&
           !string.IsNullOrWhiteSpace(values);
}