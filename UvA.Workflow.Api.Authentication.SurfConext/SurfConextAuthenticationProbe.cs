using UvA.Workflow.Api.Authentication.Abstractions;

namespace UvA.Workflow.Api.Authentication.SurfConext;

public class SurfConextAuthenticationProbe : IAuthenticationSchemeProbe
{
    public string SchemeName => SurfConextAuthenticationHandler.SchemeName;
    public int Order => 200;

    public bool CanHandle(HttpContext context)
    {
        var bearer = context.Request.Headers.Authorization.FirstOrDefault();
        return bearer?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true;
    }
}