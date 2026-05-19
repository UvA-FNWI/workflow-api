namespace UvA.Workflow.Users.EduId;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowEduIdUsers(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<EduIdOptions>(config.GetSection(EduIdOptions.Section));
        services.AddScoped<IEduIdInvitationClient, EduIdInvitationClient>();
        services.AddScoped<EduIdUserService>();
        services.AddScoped<IEduIdUserService>(sp => sp.GetRequiredService<EduIdUserService>());
        services.AddScoped<IExternalUserService>(sp => sp.GetRequiredService<EduIdUserService>());

        services.AddScoped<EduIdUserDirectory>();
        services.AddScoped<IUserRoleSource>(sp => sp.GetRequiredService<EduIdUserDirectory>());

        services.AddHttpClient(EduIdInvitationClient.HttpClientName, (provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<EduIdOptions>>().Value;
            http.BaseAddress = new Uri(options.InvitationApiUrl);
            http.DefaultRequestHeaders.Remove("X-API-TOKEN");
            http.DefaultRequestHeaders.Add("X-API-TOKEN", options.InvitationApiToken);
        });

        return services;
    }
}