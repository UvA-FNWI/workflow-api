using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.WorkflowAuthoring;
using UvA.Workflow.Api.WorkflowInstances;

namespace UvA.Workflow.Api.Infrastructure;

/// Loads requested workflow versions before the controller can fall back to the baseline.
/// Resource filters run after authentication but before controller construction.
public class WorkflowVersionFilter(ModelServiceResolver resolver, WorkflowConfigLoader loader)
    : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var version = http.Request.Headers[ModelServiceResolver.VersionHeader].FirstOrDefault();
        // Authoring resolves its own draft snapshots after checking the project grant.
        if (!string.IsNullOrEmpty(version)
            && (context.ActionDescriptor is not ControllerActionDescriptor action ||
                action.ControllerTypeInfo.AsType() != typeof(WorkflowDraftsController)))
        {
            if (version.StartsWith(WorkflowPreviewSnapshot.Prefix, StringComparison.Ordinal))
                await UseDraftPreview(http, version);
            else if (http.User.Identity?.IsAuthenticated == true && !resolver.Contains(version))
                await loader.EnsureLoadedAsync(version);
        }

        await next();
    }

    private static async Task UseDraftPreview(HttpContext http, string version)
    {
        var services = http.RequestServices;
        if (http.User.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException();

        var cache = services.GetRequiredService<IMemoryCache>();
        if (!services.GetRequiredService<IWebHostEnvironment>().IsDevOrTest() ||
            !cache.TryGetValue<WorkflowPreviewSnapshot>(version, out var preview) || preview is null)
            throw new WorkflowVersionLoadException(version, missing: true);
        if (http.Request.Headers.ContainsKey(UserImpersonationConstants.HeaderName) ||
            http.Request.Headers.ContainsKey(RoleImpersonationConstants.HeaderName))
            throw PreviewForbidden();

        var user = await services.GetRequiredService<IUserService>().GetRealUser(http.RequestAborted);
        var settings = services.GetRequiredService<IOptions<WorkflowAuthoringOptions>>().Value;
        if (user is null || user.Id != preview.UserId || !settings.Enabled ||
            !settings.Projects.TryGetValue(preview.Project, out var project) ||
            settings.Permissions(project.Folder, user.Id) is (false, false))
            throw PreviewForbidden();

        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
            throw new WorkflowAuthoringException(403, "WorkflowPreviewReadOnly", "Workflow previews are read-only.");

        ModelServiceResolver.UseDraftPreview(http, preview.Parser);
    }

    private static WorkflowAuthoringException PreviewForbidden()
        => new(403, "AuthoringForbidden", "You do not have access to this workflow preview.");
}