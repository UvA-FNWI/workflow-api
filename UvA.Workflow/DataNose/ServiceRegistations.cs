using Microsoft.Extensions.DependencyInjection;

namespace UvA.Workflow.DataNose;

public static class ServiceRegistations
{
    /// <summary>
    /// Registers the DataNose API client and its configuration options into the service collection.
    /// </summary>
    /// <param name="services">The service collection to which the DataNose API client will be added.</param>
    /// <param name="configuration">The application configuration used to bind the options for the DataNose API client.</param>
    /// <returns>The modified service collection with the DataNose API client registered.</returns>
    public static IServiceCollection AddDataNoseApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDataNoseApiClient, DataNoseApiClient>();
        
        var section = configuration.GetSection(DataNoseApiClientOptions.Section);
        
        var options = section.Get<DataNoseApiClientOptions>() ?? new DataNoseApiClientOptions();
        if (string.IsNullOrWhiteSpace(options.BaseAddress))
            throw new InvalidOperationException("BaseAddress is required");
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("ApiKey is required");
        
        services.AddHttpClient(DataNoseApiClient.Name, (sp, http) =>
        {
            http.BaseAddress = new Uri(options.BaseAddress);
            http.DefaultRequestHeaders.Remove("Api-Key");
            http.DefaultRequestHeaders.Add("Api-Key", options.ApiKey);
        });
        return services;
    }
}