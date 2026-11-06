using Microsoft.Extensions.Http.Resilience;
using Microsoft.Identity.Client;
using UvA.Workflow.Users.EntraId.Infrastructure;

namespace UvA.Workflow.Users.EntraId;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an app-only Graph client, an <see cref="IUserDirectory"/>, and an
    /// <see cref="IUserSearchSource"/> for the people picker. Incoming user tokens stay on
    /// SurfConext. Does not sync app roles.
    /// </summary>
    public static IServiceCollection AddWorkflowEntraIdUsers(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<EntraGraphOptions>(config.GetSection(EntraGraphOptions.Section));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EntraGraphOptions>>().Value;
            options.Validate();
            return options;
        });

        services.AddHttpClient("EntraGraph", (provider, client) =>
            {
                var graphOptions = provider.GetRequiredService<EntraGraphOptions>();
                client.Timeout = TimeSpan.FromSeconds(graphOptions.RequestTimeoutSeconds);
            })
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.Retry.MaxRetryAttempts = 2;
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            });

        services.AddSingleton<IConfidentialClientApplication>(provider =>
        {
            var options = provider.GetRequiredService<EntraGraphOptions>();
            return ConfidentialClientApplicationBuilder
                .Create(options.ClientId)
                .WithClientSecret(options.ClientSecret)
                .WithAuthority(options.Authority)
                .Build();
        });

        services.AddSingleton<IEntraGraphService, EntraGraphService>();
        services.AddScoped<IUserDirectory, EntraUserDirectory>();
        services.AddScoped<IUserSearchSource, EntraUserSearchSource>();

        return services;
    }
}