using Microsoft.AspNetCore.Authentication;
using Microsoft.OpenApi;

namespace UvA.Workflow.Api.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddWorkflowAuthenticationSelector(this IServiceCollection services,
        IWebHostEnvironment environment,
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

        services.AddSwaggerGen(c =>
        {
            if (environment.IsDevOrTest())
            {
                c.AddSecurityDefinition("OIDC",
                    new OpenApiSecurityScheme
                    {
                        Name = "Authorization",
                        BearerFormat = "JWT",
                        Scheme = "Bearer",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.OAuth2,
                        Flows = new OpenApiOAuthFlows
                        {
                            AuthorizationCode = new OpenApiOAuthFlow
                            {
                                AuthorizationUrl = new Uri("https://auth-pr.datanose.nl/auth"),
                                TokenUrl = new Uri("https://auth-pr.datanose.nl/token")
                            }
                        }
                    });
            }

            c.AddSecurityDefinition("Bearer",
                new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    BearerFormat = "JWT",
                    Scheme = "Bearer",
                    Description = "Specify the authorization token",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http
                });

            c.AddSecurityDefinition("Api-Key",
                new OpenApiSecurityScheme
                {
                    Description = "Enter your API key.",
                    Name = "Api-Key",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey
                });

            c.AddSecurityRequirement(doc =>
            {
                var securityRequirement = new OpenApiSecurityRequirement();
                if (environment.IsDevOrTest())
                {
                    securityRequirement.Add(new OpenApiSecuritySchemeReference("OIDC", doc), []);
                }

                securityRequirement.Add(new OpenApiSecuritySchemeReference("Bearer", doc), []);
                securityRequirement.Add(new OpenApiSecuritySchemeReference("Api-Key", doc), []);
                return securityRequirement;
            });
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