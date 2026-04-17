using Microsoft.AspNetCore.Authentication;
using UvA.Workflow.Api.Authentication.Abstractions;
using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddWorkflowAuthenticationSelector(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddAuthorization();
        services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
        services.AddSingleton<IAuthenticationSchemeProbe, ApiKeyAuthenticationProbe>();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, NoResultAuthenticationHandler>(
                WorkflowAuthenticationDefaults.NoResultScheme,
                _ => { })
            .AddApiKeyAuthentication(options =>
            {
                configuration.GetSection(ApiKeyAuthenticationOptions.Section).Bind(options);
            })
            .AddPolicyScheme(WorkflowAuthenticationDefaults.UserScheme,
                WorkflowAuthenticationDefaults.UserScheme,
                options =>
                {
                    options.ForwardDefaultSelector = context => SelectScheme(context, includeApiKey: false);
                    options.ForwardDefault = WorkflowAuthenticationDefaults.NoResultScheme;
                })
            .AddPolicyScheme(WorkflowAuthenticationDefaults.AnyScheme,
                WorkflowAuthenticationDefaults.AnyScheme,
                options =>
                {
                    options.ForwardDefaultSelector = context => SelectScheme(context, includeApiKey: true);
                    options.ForwardDefault = WorkflowAuthenticationDefaults.NoResultScheme;
                });

        return services;
    }

    public static IApplicationBuilder UseWorkflowAuthenticationSelector(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    private static string? SelectScheme(HttpContext context, bool includeApiKey)
    {
        var probes = context.RequestServices.GetServices<IAuthenticationSchemeProbe>()
            .OrderBy(p => p.Order);

        foreach (var probe in probes)
        {
            if (!includeApiKey &&
                string.Equals(probe.SchemeName, ApiKeyAuthenticationHandler.AuthenticationScheme,
                    StringComparison.Ordinal))
                continue;

            if (probe.CanHandle(context))
                return probe.SchemeName;
        }

        return null;
    }
}