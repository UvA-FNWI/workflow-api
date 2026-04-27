using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.DataNose;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Infrastructure.Database;
using UvA.Workflow.Infrastructure.S3;
using UvA.Workflow.Jobs;
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
        services.Configure<EduIdOptions>(config.GetSection(EduIdOptions.Section));
        var graphMailOptions = config.GetSection(GraphMailOptions.Section).Get<GraphMailOptions>() ??
                               new GraphMailOptions();
        GraphMailOptions.Validate(graphMailOptions);
        services.Configure<EncryptionServiceConfig>(config.GetSection(EncryptionServiceConfig.SectionName));

        // Configure S3
        services.Configure<S3Config>(config.GetSection(S3Config.S3));

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
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddUserSources();
        services.AddScoped<IEduIdInvitationClient, EduIdInvitationClient>();
        services.AddScoped<IEduIdUserService, EduIdUserService>();

        services.AddHttpClient(EduIdInvitationClient.HttpClientName, (provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<EduIdOptions>>().Value;
            http.BaseAddress = new Uri(options.InvitationApiUrl);
            http.DefaultRequestHeaders.Remove("X-API-TOKEN");
            http.DefaultRequestHeaders.Add("X-API-TOKEN", options.InvitationApiToken);
        });

        services.AddScoped<WorkflowInstanceService>();

        services.AddScoped<IUserService, UserService>();

        services.AddScoped<ModelService>(sp => sp.GetRequiredService<ModelServiceResolver>().Get());

        services.AddScoped<IArtifactService, S3ArtifactService>();
        services.AddScoped<IArtifactTokenService, S3ArtifactTokenService>();

        services.AddScoped<AnswerService>();
        services.AddScoped<SubmissionService>();
        services.AddScoped<SubmissionDtoFactory>();
        services.AddScoped<AnswerDtoFactory>();
        services.AddScoped<StepHeaderStatusResolver>();

        services.AddScoped<InstanceService>();
        services.AddScoped<IInstanceEventService, InstanceEventService>();
        services.AddScoped<IStepVersionService, StepVersionService>();

        services.AddScoped<RightsService>();
        services.AddScoped<JobService>();
        services.AddScoped<EffectService>();
        services.AddScoped<AnswerConversionService>();
        services.AddScoped<InitializationService>();

        services.AddScoped<IMailService, GraphMailService>();
        services.AddScoped<INamedMailLayout, DefaultMailLayout>();
        services.AddScoped<IMailLayoutResolver, MailLayoutResolver>();
        services.AddScoped<MailBuilder>();
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

        services.AddHostedService<JobWorker>();

        return services;
    }

    private static IServiceCollection AddUserSources(this IServiceCollection services)
    {
        services.AddScoped<IUserRoleSource, DataNoseUserRoleSource>();
        services.AddScoped<IUserSearchSource, DataNoseUserSearchSource>();

        // Register once so both interfaces resolve to the same scoped EduId directory instance.
        services.AddScoped<EduIdUserDirectory>();
        services.AddScoped<IUserRoleSource>(sp => sp.GetRequiredService<EduIdUserDirectory>());
        services.AddScoped<IUserSearchSource>(sp => sp.GetRequiredService<EduIdUserDirectory>());

        return services;
    }
}