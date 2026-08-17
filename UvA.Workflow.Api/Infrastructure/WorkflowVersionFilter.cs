using Microsoft.AspNetCore.Mvc.Filters;

namespace UvA.Workflow.Api.Infrastructure;

/// Loads requested workflow versions before the controller can fall back to the baseline.
/// Resource filters run after authentication but before controller construction.
public class WorkflowVersionFilter(ModelServiceResolver resolver, WorkflowConfigLoader loader)
    : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var version = context.HttpContext.Request.Headers[ModelServiceResolver.VersionHeader].FirstOrDefault();
        // Do not let anonymous endpoints trigger repository fetches.
        if (!string.IsNullOrEmpty(version)
            && context.HttpContext.User.Identity?.IsAuthenticated == true
            && !resolver.Contains(version))
            await loader.EnsureLoadedAsync(version);

        await next();
    }
}