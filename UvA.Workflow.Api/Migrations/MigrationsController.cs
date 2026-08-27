using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

[Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
public class MigrationsController(
    MigrationService migrationService,
    RightsService rightsService,
    ICurrentUserAccessor currentUserAccessor) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MigrationDto>>> Get(CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        return Ok((await migrationService.GetAll(ct)).Select(MigrationDto.Create).ToArray());
    }

    [HttpPost("PropertyRename")]
    public async Task<ActionResult<MigrationDto>> CreatePropertyRename(
        CreatePropertyRenameDto input,
        CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        var migration = await migrationService.CreatePropertyRename(
            input.WorkflowDefinitions,
            input.OldProperty,
            input.NewProperty,
            currentUserAccessor.GetCurrentUserName() ?? "unknown administrator",
            ct);
        return Ok(MigrationDto.Create(migration));
    }

    [HttpPost("{id}/Finish")]
    public async Task<ActionResult<MigrationDto>> Finish(string id, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        return Ok(MigrationDto.Create(await migrationService.Finish(id, ct)));
    }

    [HttpPost("{id}/Revert")]
    public async Task<ActionResult<MigrationDto>> Revert(string id, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        return Ok(MigrationDto.Create(await migrationService.Revert(id, ct)));
    }
}