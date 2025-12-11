using Microsoft.OpenApi;
using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.Authentication;

public static class AuthenticationExtensions
{
    public const string AllSchemes = "SURFconext,ApiKey";

    public static IServiceCollection AddWorkflowAuthentication(this IServiceCollection services,
        IWebHostEnvironment environment, IConfiguration configuration)
    {
        const string authSelector = "AuthSelector";

        services.AddSurfConextServices(configuration);

        services.AddAuthentication(authSelector)
            .AddApiKeyAuthentication(options =>
            {
                configuration.GetSection(ApiKeyAuthenticationOptions.Section).Bind(options);
            })
            .AddSurfConext(options => { configuration.GetSection(SurfConextOptions.Section).Bind(options); })
            .AddPolicyScheme(authSelector,
                authSelector,
                options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        if (context.Request.Headers.TryGetValue("Api-Key", out var values) &&
                            !string.IsNullOrWhiteSpace(values))
                            return ApiKeyAuthenticationHandler.AuthenticationScheme;

                        return SurfConextAuthenticationHandler.SchemeName;
                    };
                });

        services.AddSwaggerGen(c =>
        {
            if (environment.IsDevOrTest()) // OIDC (SURFconext) authentication
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

            // Bearer token authentication
            c.AddSecurityDefinition("Bearer",
                new OpenApiSecurityScheme()
                {
                    Name = "Authorization",
                    BearerFormat = "JWT",
                    Scheme = "Bearer",
                    Description = "Specify the authorization token",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.Http,
                });

            // API key via Authorization header with 'ApiKey' prefix
            c.AddSecurityDefinition("Api-Key",
                new OpenApiSecurityScheme
                {
                    Description = "Enter your API key.",
                    Name = "Api-Key",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey
                });

            //New code to work with .NET6
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
}