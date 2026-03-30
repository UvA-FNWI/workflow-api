using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using UvA.Workflow.Api.Infrastructure;
using UvA.LTI;

namespace UvA.Workflow.Api.Authentication;

public static class AuthenticationExtensions
{
    public const string AllSchemes = $"{SurfConextAuthenticationHandler.SchemeName}," +
                                     $"{ApiKeyAuthenticationHandler.AuthenticationScheme}," +
                                     $"{CanvasLtiDefaults.AuthenticationScheme}";

    public static IServiceCollection AddWorkflowAuthentication(this IServiceCollection services,
        IWebHostEnvironment environment, IConfiguration configuration)
    {
        const string authSelector = "AuthSelector";

        services.AddSurfConextServices(configuration);
        services.Configure<CanvasLtiOptions>(configuration.GetSection(CanvasLtiOptions.Section));
        services.AddScoped<ILtiClaimsResolver, CanvasClaimsResolver>();
        services.AddScoped<CanvasLaunchTargetResolver>();

        services.AddAuthentication(authSelector)
            .AddApiKeyAuthentication(options =>
            {
                configuration.GetSection(ApiKeyAuthenticationOptions.Section).Bind(options);
            })
            .AddSurfConext(options => { configuration.GetSection(SurfConextOptions.Section).Bind(options); })
            .AddJwtBearer(CanvasLtiDefaults.AuthenticationScheme,
                options =>
                {
                    var ltiOptions = configuration.GetSection(CanvasLtiOptions.Section).Get<CanvasLtiOptions>()
                                     ?? throw new InvalidOperationException(
                                         $"Missing {CanvasLtiOptions.Section} configuration");

                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = false,
                        ValidIssuer = CanvasLtiDefaults.Issuer,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(ltiOptions.Key)),
                        NameClaimType = CanvasClaimTypes.UvanetId,
                        RoleClaimType = CanvasClaimTypes.Role
                    };
                })
            .AddPolicyScheme(authSelector,
                authSelector,
                options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        if (context.Request.Headers.TryGetValue("Authorization", out var authorizationHeader) &&
                            authorizationHeader.FirstOrDefault() is { } bearerHeader &&
                            bearerHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            var token = bearerHeader[7..].Trim();
                            var tokenHandler = new JwtSecurityTokenHandler();
                            if (tokenHandler.CanReadToken(token))
                            {
                                var jwt = tokenHandler.ReadJwtToken(token);
                                if (string.Equals(jwt.Issuer, CanvasLtiDefaults.Issuer,
                                        StringComparison.OrdinalIgnoreCase))
                                    return CanvasLtiDefaults.AuthenticationScheme;
                            }
                        }

                        if (context.Request.Headers.TryGetValue("Api-Key", out var values) &&
                            !string.IsNullOrWhiteSpace(values))
                            return ApiKeyAuthenticationHandler.AuthenticationScheme;

                        return SurfConextAuthenticationHandler.SchemeName;
                    };
                });

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
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

    public static IApplicationBuilder UseWorkflowAuthentication(this IApplicationBuilder app, IConfiguration config)
    {
        var ltiOptions = config.GetSection(CanvasLtiOptions.Section).Get<CanvasLtiOptions>()
                         ?? throw new InvalidOperationException(
                             $"Missing {CanvasLtiOptions.Section} configuration");

        app.UseForwardedHeaders();
        app.UseLti(new LtiOptions
        {
            ClientId = ltiOptions.ClientId,
            AuthenticateUrl = ltiOptions.AuthenticateUrl,
            JwksUrl = ltiOptions.JwksUrl,
            SigningKey = ltiOptions.Key,
            TokenLifetime = ltiOptions.TokenLifetime,
            RedirectUrl = $"{config["FrontendBaseUrl"]?.TrimEnd('/')}/canvas",
            RedirectFunction = form => IsCanvasProduction(form) ? null : ltiOptions.TestRedirectUrl
        });

        return app;
    }

    private static bool IsCanvasProduction(IFormCollection parameters)
        => parameters["canvas_environment"].FirstOrDefault()?.StartsWith("prod", StringComparison.OrdinalIgnoreCase) ==
           true;
}