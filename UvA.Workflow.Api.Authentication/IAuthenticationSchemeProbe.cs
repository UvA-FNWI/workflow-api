using Microsoft.AspNetCore.Http;

namespace UvA.Workflow.Api.Authentication;

public interface IAuthenticationSchemeProbe
{
    string SchemeName { get; }
    int Order { get; }
    bool CanHandle(HttpContext context);
}
