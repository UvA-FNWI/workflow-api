using Microsoft.Extensions.DependencyInjection;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure.S3;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Submissions;
using UvA.Workflow.Versioning;

namespace UvA.Workflow;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowCore(this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();

        services.AddScoped<ICurrentUserAccessor, NullCurrentUserAccessor>();
        services.AddScoped<IUserService, UserService>();

        services.AddScoped<WorkflowInstanceService>();
        services.AddScoped<InstanceService>();
        services.AddScoped<IInstanceEventService, InstanceEventService>();
        services.AddScoped<IStepVersionService, StepVersionService>();

        services.Configure<S3Config>(config.GetSection(S3Config.S3));
        services.AddScoped<IArtifactService, S3ArtifactService>();

        services.AddScoped<IMailService, DummyMailService>();
        services.AddScoped<INamedMailLayout, DefaultMailLayout>();
        services.AddScoped<IMailLayoutResolver, MailLayoutResolver>();
        services.AddScoped<MailBuilder>();

        services.AddScoped<AnswerService>();
        services.AddScoped<SubmissionService>();
        services.AddScoped<RightsService>();
        services.AddScoped<JobService>();
        services.AddScoped<EffectService>();
        services.AddScoped<AnswerConversionService>();
        services.AddScoped<InitializationService>();

        services.AddHostedService<JobWorker>();

        return services;
    }
}