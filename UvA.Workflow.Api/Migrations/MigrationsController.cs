using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
public class MigrationsController(
    ModelServiceResolver modelServiceResolver,
    IMigrationStore migrationStore,
    RightsService rightsService)
    : ApiControllerBase
{
    /// <summary>Returns migration plans and progress without changing configuration or data.</summary>
    [HttpGet]
    public async Task<ActionResult<MigrationOverviewDto>> Get(CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var activeConfiguration = modelServiceResolver.GetVersions()
            .SingleOrDefault(version => version.Name == "");
        var migrations = await migrationStore.GetAll(ct);

        return Ok(new MigrationOverviewDto(
            activeConfiguration,
            modelServiceResolver.GetPendingBaseline(),
            modelServiceResolver.GetBaselineMigrationPlans().Select(MigrationPlanDto.Create).ToArray(),
            modelServiceResolver.GetPendingMigrationPlans().Select(MigrationPlanDto.Create).ToArray(),
            migrations.Select(MigrationDto.Create).ToArray()));
    }
}