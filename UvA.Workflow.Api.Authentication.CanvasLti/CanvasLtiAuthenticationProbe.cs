using System.IdentityModel.Tokens.Jwt;
using UvA.Workflow.Api.Authentication.Abstractions;

namespace UvA.Workflow.Api.Authentication.CanvasLti;

public class CanvasLtiAuthenticationProbe : IAuthenticationSchemeProbe
{
    public string SchemeName => CanvasLtiDefaults.AuthenticationScheme;
    public int Order => 100;

    public bool CanHandle(HttpContext context)
    {
        var bearer = context.Request.Headers.Authorization.FirstOrDefault();
        if (bearer?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) != true)
            return false;

        var token = bearer[7..].Trim();
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
            return false;

        return string.Equals(handler.ReadJwtToken(token).Issuer, CanvasLtiDefaults.Issuer,
            StringComparison.OrdinalIgnoreCase);
    }
}