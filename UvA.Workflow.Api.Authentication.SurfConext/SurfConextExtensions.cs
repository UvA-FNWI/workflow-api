using System.Text;
using UvA.Workflow.Api.Authentication.Abstractions;

namespace UvA.Workflow.Api.Authentication.SurfConext;

public static class SurfConextExtensions
{
    public static IServiceCollection AddWorkflowSurfConextAuthentication(this IServiceCollection services,
        IConfiguration config)
    {
        services.AddMemoryCache();
        services.AddSingleton<IAuthenticationSchemeProbe, SurfConextAuthenticationProbe>();

        var options = config.GetSection(SurfConextOptions.Section).Get<SurfConextOptions>();

        if (string.IsNullOrEmpty(options?.BaseUrl))
            throw new InvalidOperationException("Missing SurfConextOptions.BaseUrl");
        if (string.IsNullOrEmpty(options.ClientId))
            throw new InvalidOperationException("Missing SurfConextOptions.ClientId");
        if (string.IsNullOrEmpty(options.ClientSecret))
            throw new InvalidOperationException("Missing SurfConextOptions.ClientSecret");

        services.AddHttpClient(SurfConextAuthenticationHandler.SchemeName, client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl!);
            client.DefaultRequestHeaders.Add("Authorization",
                $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ClientId}:{options.ClientSecret}"))}");
        });

        services.AddAuthentication()
            .AddSurfConext(bound =>
            {
                config.GetSection(SurfConextOptions.Section).Bind(bound);
            });

        return services;
    }

    public static AuthenticationBuilder AddSurfConext(this AuthenticationBuilder builder,
        Action<SurfConextOptions> options)
    {
        return builder.AddScheme<SurfConextOptions, SurfConextAuthenticationHandler>(
            SurfConextAuthenticationHandler.SchemeName,
            null,
            options
        );
    }
}
