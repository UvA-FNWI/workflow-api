using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

public interface IConfiguredMigrationRunner
{
    Task Run(ModelParser parser, CancellationToken ct = default);
}

public class ConfiguredMigrationRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<ConfiguredMigrationRunner> logger) : IConfiguredMigrationRunner
{
    public async Task Run(ModelParser parser, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMigrationRepository>();
        var service = new MigrationService(new ModelService(parser), repository);

        foreach (var migration in parser.Migrations.OrderBy(value => value.MigrationId, StringComparer.Ordinal))
        {
            var result = await service.RunConfigured(migration, ct);
            logger.LogInformation("Configured migration {MigrationId} has status {Status}",
                result.MigrationId, result.Status);
        }
    }
}