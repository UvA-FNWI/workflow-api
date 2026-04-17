using Microsoft.AspNetCore.Http;

namespace UvA.Workflow.Api.Infrastructure;

public class HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public string? GetCurrentUserName()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        return principal?.Identity?.IsAuthenticated == true
            ? principal.Identity.Name
            : null;
    }
}