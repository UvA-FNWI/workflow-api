using Microsoft.AspNetCore.Http;

namespace UvA.Workflow.Api.Authentication.Abstractions;

public interface IAuthenticationSchemeProbe
{
    string SchemeName { get; }
    int Order { get; }
    bool CanHandle(HttpContext context);
}