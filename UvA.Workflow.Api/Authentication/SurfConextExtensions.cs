
using System.Text;
using Microsoft.OpenApi.Models;
using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.Authentication;

public static class SurfConextExtensions
{
    public static IServiceCollection AddSurfConextAuthentication(this IServiceCollection services,IWebHostEnvironment environment, IConfiguration config)
    {
        services.AddMemoryCache();
        
        var options=config.GetSection(SurfConextOptions.Section).Get<SurfConextOptions>();
        if (options == null || !options.IsValid())
            throw new InvalidOperationException("Missing or invalid SurfConextOptions");
        
        services.Configure<SurfConextOptions>(config.GetSection(SurfConextOptions.Section));

        services.AddHttpClient<SurfConextAuthenticationHandler>(client =>
        {
            client.BaseAddress = new Uri(options.IntrospectUrl!);
            client.DefaultRequestHeaders.Add("Authorization",
                $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ClientId}:{options.Authorization}"))}");
        });
                
        services.AddAuthentication(authOptions =>
        {
            authOptions.AddScheme<SurfConextAuthenticationHandler>(SurfConextAuthenticationHandler.Scheme, null);
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

            //New code to work with .NET6
            var securityRequirement = new OpenApiSecurityRequirement();
            if (environment.IsDevOrTest())
            {
                securityRequirement.Add(new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "OIDC"
                        }
                    },
                    []);
            }

            securityRequirement.Add(new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                []);

            c.AddSecurityRequirement(securityRequirement);

            c.CustomSchemaIds(x =>
            {
                var name = x.FullName;
                var parts = name!.Split(".");
                if (parts.Length > 3 && parts[0].ToLower() == "uva" && parts[1].ToLower() == "datanose")
                    return parts[2] + "." + parts.Last();
                return name;
            });
        });

        return services;
    }
}