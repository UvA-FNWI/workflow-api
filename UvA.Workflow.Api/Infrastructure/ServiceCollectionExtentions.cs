using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Infrastructure.Database;
using UvA.Workflow.Persistence;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Infrastructure;

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
            return client.GetDatabase(options.Database);
        });

        // Register repositories - organized by domain feature
        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        services.AddScoped<WorkflowInstanceService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ModelService>(sp => sp.GetRequiredService<ModelServiceResolver>().Get());

        services.AddScoped<IArtifactService,ArtifactService>();
        services.AddScoped<AnswerService>();
        services.AddScoped<SubmissionService>();
        services.AddScoped<ArtifactTokenService>();
        services.AddScoped<SubmissionDtoFactory>();
        services.AddScoped<AnswerDtoFactory>();

        services.AddScoped<InstanceService>();
        services.AddScoped<RightsService>();
        services.AddScoped<TriggerService>();
        services.AddScoped<AnswerConversionService>();

        services.AddScoped<IMailService, DummyMailService>();

        services.AddSingleton<ModelServiceResolver>();
        services.AddScoped<ScreenDataService>();

        return services;
    }
}