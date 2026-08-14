using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
public class MigrationsController(
    ModelServiceResolver modelServiceResolver,
    WorkflowConfigLoader configLoader,
    IMigrationStore migrationStore,
    RightsService rightsService,
    ICurrentUserAccessor currentUserAccessor)
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
            migrations.Select(MigrationDto.Create).ToArray()));
    }

    /// <summary>
    /// Publishes the pending rename mappings. The new configuration remains pending until all instance values
    /// have been copied and verified.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateMigrationResponseDto>> Create(
        CreateMigrationDto input,
        CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        try
        {
            var migration = await configLoader.CreateMigrationAsync(
                migrationStore,
                input.Name,
                input.Kind,
                input.WorkflowDefinition,
                input.OldPath,
                input.NewPath,
                input.TargetRef,
                input.Description,
                currentUserAccessor.GetCurrentUserName() ?? "unknown administrator",
                ct);
            var pendingCommit = modelServiceResolver.GetPendingBaseline()!.TargetCommit;
            return Ok(new CreateMigrationResponseDto(
                pendingCommit,
                "Migration created. The current configuration remains active while existing data is copied to the new field.",
                MigrationDto.Create(migration)));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }
    }
}