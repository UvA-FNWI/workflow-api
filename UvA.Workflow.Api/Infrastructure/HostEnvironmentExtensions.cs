namespace UvA.Workflow.Api.Infrastructure;

public static class HostEnvironmentExtensions
{
    public static bool EnableExperimentalFeatures(this IHostEnvironment env) => !env.IsProduction();

    public static bool IsDevOrTest(this IWebHostEnvironment environment)
        => environment.IsDevelopment() || environment.IsEnvironment("Test");

    public static bool IsStagingOrAcceptance(this IWebHostEnvironment environment)
        => environment.IsStaging() || environment.IsEnvironment("Acceptance");
}