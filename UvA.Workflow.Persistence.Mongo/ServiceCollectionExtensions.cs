namespace UvA.Workflow.Persistence.Mongo;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowMongoPersistence(this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<MongoOptions>(config.GetSection("Mongo"));

        services.AddSingleton<IMongoDatabase>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;
            var client = new MongoClient(options.ConnectionString);
            return client.GetDatabase(options.Database);
        });

        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IInstanceEventRepository, InstanceEventRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IInstanceJournalService, InstanceJournalService>();
        services.AddScoped<IMailLogRepository, MailLogRepository>();
        services.AddScoped<ISettingsStore, SettingsStore>();
        services.AddScoped<IArtifactService, ArtifactService>();

        return services;
    }
}