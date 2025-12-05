using System.Text;
using Microsoft.OpenApi;
using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.Authentication;

public static class SurfConextExtensions
{
    public static IServiceCollection AddSurfConextAuthentication(this IServiceCollection services,
        IWebHostEnvironment environment, IConfiguration config)
    {
        services.AddMemoryCache();

        var options = config.GetSection(SurfConextOptions.Section).Get<SurfConextOptions>();

        if (string.IsNullOrEmpty(options?.BaseUrl))
            throw new InvalidOperationException("Missing SurfConextOptions.BaseUrl");
        if (string.IsNullOrEmpty(options.ClientId))
            throw new InvalidOperationException("Missing SurfConextOptions.ClientId");
        if (string.IsNullOrEmpty(options.ClientSecret))
            throw new InvalidOperationException("Missing SurfConextOptions.ClientSecret");

        services.Configure<SurfConextOptions>(config.GetSection(SurfConextOptions.Section));

        services.AddHttpClient<SurfConextAuthenticationHandler>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl!);
            client.DefaultRequestHeaders.Add("Authorization",
                $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ClientId}:{options.ClientSecret}"))}");
        });

        services.AddAuthentication(authOptions =>
        {
            authOptions.AddScheme<SurfConextAuthenticationHandler>(SurfConextAuthenticationHandler.SchemeName,
                null);
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

            c.AddSecurityRequirement(doc =>
            {
                var securityRequirement = new OpenApiSecurityRequirement();
                if (environment.IsDevOrTest())
                {
                    securityRequirement.Add(new OpenApiSecuritySchemeReference("OIDC", doc), []);
                }

                securityRequirement.Add(new OpenApiSecuritySchemeReference("Bearer", doc), []);
                return securityRequirement;
            });
        });

        return services;
    }
}