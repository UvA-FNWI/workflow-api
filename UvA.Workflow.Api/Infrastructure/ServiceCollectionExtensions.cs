using System.Net.Http.Headers;
using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.WorkflowInstances.Dtos;

namespace UvA.Workflow.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowApiCore(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddSingleton<ModelServiceResolver>();
        services.AddScoped<ModelService>(sp => sp.GetRequiredService<ModelServiceResolver>().Get());

        services.AddSingleton<WorkflowConfigLoader>();
        services.AddHttpClient(nameof(WorkflowConfigLoader), (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WorkflowSourceOptions>>().Value;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("workflow-api"); // GitHub API requires a UA
            if (!string.IsNullOrWhiteSpace(opts.Token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.Token);
        });

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