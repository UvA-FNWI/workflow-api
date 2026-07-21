using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Notifications;

namespace UvA.Workflow.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowApiCore(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddSingleton<ModelServiceResolver>();
        services.AddScoped(sp => sp.GetRequiredService<ModelServiceResolver>().Resolve());
        services.AddScoped(sp => sp.GetRequiredService<ResolvedWorkflowConfig>().ModelService);
        services.Replace(ServiceDescriptor.Scoped<MailTemplateStore>(sp => new MailTemplateStore
        {
            Default = sp.GetRequiredService<ResolvedWorkflowConfig>().DefaultMailLayout
        }));

        services.AddSingleton<WorkflowConfigLoader>();
        services.AddHttpClient(nameof(WorkflowConfigLoader), (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WorkflowSourceOptions>>().Value;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("workflow-api");
            if (!string.IsNullOrWhiteSpace(opts.Token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.Token);
        });
        services.AddHostedService<WorkflowConfigPoller>();

        services.AddScoped<ArtifactTokenService>();
        services.AddScoped<SubmissionDtoFactory>();
        services.AddScoped<AnswerDtoFactory>();
        services.AddScoped<AssessmentDtoFactory>();
        services.AddScoped<StepHeaderStatusResolver>();
        services.AddScoped<WorkflowInstanceDtoFactory>();

        services.AddScoped<ScreenDataService>();
        services.AddScoped<InstanceAuthorizationFilterService>();
        services.AddScoped<RoleImpersonationService>();
        services.AddScoped<IImpersonationContextService>(sp => sp.GetRequiredService<RoleImpersonationService>());
        services.AddScoped<ExternalUserEmailUpdateService>();

        return services;
    }
}