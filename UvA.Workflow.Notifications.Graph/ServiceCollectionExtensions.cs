using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Notifications.Graph;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowGraphMail(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GraphMailOptions>(config.GetSection(GraphMailOptions.Section));
        var options = config.GetSection(GraphMailOptions.Section).Get<GraphMailOptions>() ?? new GraphMailOptions();
        GraphMailOptions.Validate(options);

        services.Configure<EncryptionServiceConfig>(config.GetSection(EncryptionServiceConfig.SectionName));
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<IGraphMailTokenStore, GraphMailTokenStore>();
        services.AddScoped<IMailService, GraphMailService>();

        return services;
    }
}