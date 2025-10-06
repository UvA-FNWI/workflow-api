using UvA.Workflow.Infrastructure.Database;
using UvA.Workflow.Infrastructure.Persistence;

namespace UvA.Workflow.Api.Tools;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflow(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();

        // Configure MongoDB
        services.Configure<MongoOptions>(config.GetSection("Mongo"));

        // Register MongoDB database
        services.AddSingleton<IMongoDatabase>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;
            var client = new MongoClient(options.ConnectionString);
            return client.GetDatabase(options.DatabaseName);
        });

        // Register repositories - organized by domain feature
        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        services.AddScoped<WorkflowInstanceService>();
        services.AddScoped<UserService>();
        services.AddSingleton<ModelService>();

        services.AddScoped<FileClient>();
        services.AddScoped<FileService>();

        services.AddScoped<ContextService>();
        services.AddScoped<InstanceService>();
        services.AddScoped<RightsService>();

        services.AddSingleton(
            new ModelParser("/Users/annesnegmel-din/code/work/workflow-api/Uva.Workflow/Example/Projects"));


        return services;
    }
}