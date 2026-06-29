using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Submissions.Dtos;
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

        return services;
    }
}