using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using UvA.LTI;
using UvA.Workflow.Api.Authentication.Abstractions;

namespace UvA.Workflow.Api.Authentication.CanvasLti;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowCanvasLtiAuthentication(this IServiceCollection services,
        IWebHostEnvironment environment, IConfiguration configuration)
    {
        services.Configure<CanvasLtiOptions>(configuration.GetSection(CanvasLtiOptions.Section));
        services.AddScoped<ILtiClaimsResolver, CanvasClaimsResolver>();
        services.AddScoped<CanvasLaunchTargetResolver>();
        services.AddSingleton<IAuthenticationSchemeProbe, CanvasLtiAuthenticationProbe>();

        services.AddAuthentication()
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
                });

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddSwaggerGen(c =>
        {
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
                var securityRequirement = new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("Bearer", doc), [] }
                };
                return securityRequirement;
            });
        });

        return services;
    }

    public static IApplicationBuilder UseWorkflowCanvasLti(this IApplicationBuilder app, IConfiguration config)
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