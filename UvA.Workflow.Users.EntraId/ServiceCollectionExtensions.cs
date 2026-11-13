using Microsoft.Extensions.Http.Resilience;
using UvA.Workflow.Users.EntraId.Infrastructure;

namespace UvA.Workflow.Users.EntraId;

public static class ServiceCollectionExtensions
{
    public const string HttpClientName = "EntraGraph";

    /// <summary>
    /// Registers an app-only Graph client, an <see cref="IUserDirectory"/>, and an
    /// <see cref="IUserSearchSource"/> for the people picker. Incoming user tokens stay on
    /// SurfConext. Does not sync app roles.
    /// </summary>
    public static IServiceCollection AddWorkflowEntraIdUsers(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(EntraGraphOptions.Section);
        var bound = section.Get<EntraGraphOptions>() ?? new EntraGraphOptions();
        bound.BindTenants(section);
        bound.Validate();

        services.AddOptions<EntraGraphOptions>()
            .Bind(section)
            .PostConfigure(options => options.BindTenants(section))
            .ValidateOnStart();
        services.Configure<SystemAdminBootstrapOptions>(config.GetSection(SystemAdminBootstrapOptions.Section));
        services.AddSingleton<IValidateOptions<EntraGraphOptions>, EntraGraphOptionsValidator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<EntraGraphOptions>>().Value);

        services.AddHttpClient(HttpClientName, client =>
            {
                // The resilience handler owns the deadlines. A finite HttpClient.Timeout
                // would cancel the call before retries finish.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.Retry.MaxRetryAttempts = 2;
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(EntraGraphOptions.AttemptTimeoutSeconds);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(bound.RequestTimeoutSeconds);
            });

        services.AddSingleton<IEntraAccessTokenSource, MsalEntraAccessTokenSource>();
        services.AddSingleton<IEntraGraphService, EntraGraphService>();
        services.AddScoped<IUserDirectory, EntraUserDirectory>();
        services.AddScoped<IUserSearchSource, EntraUserSearchSource>();

        return services;
    }
}