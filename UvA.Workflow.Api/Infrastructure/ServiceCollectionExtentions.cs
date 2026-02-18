using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Infrastructure.Database;
using UvA.Workflow.Journaling;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Submissions;
using UvA.Workflow.Versioning;

namespace UvA.Workflow.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflow(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();

        // Configure and validate Graph mail settings
        services.Configure<GraphMailOptions>(config.GetSection(GraphMailOptions.Section));
        var graphMailOptions = config.GetSection(GraphMailOptions.Section).Get<GraphMailOptions>() ??
                               new GraphMailOptions();
        GraphMailOptions.Validate(graphMailOptions);
        services.Configure<EncryptionServiceConfig>(config.GetSection(EncryptionServiceConfig.SectionName));

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
        services.AddScoped<IInstanceEventRepository, InstanceEventRepository>();

        services.AddScoped<WorkflowInstanceService>();

        // TODO: restore actual user service when UI has SurfContext support
        services.AddScoped<IUserService, MockUserService>();
        //services.AddScoped<IUserService, MockUserService>();

        services.AddScoped<ModelService>(sp => sp.GetRequiredService<ModelServiceResolver>().Get());

        services.AddScoped<IArtifactService, ArtifactService>();
        services.AddScoped<AnswerService>();
        services.AddScoped<SubmissionService>();
        services.AddScoped<ArtifactTokenService>();
        services.AddScoped<SubmissionDtoFactory>();
        services.AddScoped<AnswerDtoFactory>();

        services.AddScoped<InstanceService>();
        services.AddScoped<IInstanceEventService, InstanceEventService>();
        services.AddScoped<IStepVersionService, StepVersionService>();

        services.AddScoped<RightsService>();
        services.AddScoped<EffectService>();
        services.AddScoped<AnswerConversionService>();
        services.AddScoped<InitializationService>();

        services.AddScoped<IMailService, GraphMailService>();
        services.AddScoped<IMailLogRepository, MailLogRepository>();
        services.AddScoped<ISettingsStore, SettingsStore>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<IGraphMailTokenStore, GraphMailTokenStore>();

        services.AddSingleton<ModelServiceResolver>();
        services.AddScoped<ScreenDataService>();
        services.AddScoped<InstanceAuthorizationFilterService>();
        services.AddScoped<ImpersonationService>();
        services.AddScoped<IImpersonationContextService>(sp => sp.GetRequiredService<ImpersonationService>());

        services.AddScoped<IInstanceJournalService, InstanceJournalService>();
        services.AddScoped<InstanceEventService>();

        return services;
    }
}