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
        return await Run(() => migrationService.CreatePropertyRename(
            input.WorkflowDefinitions,
            input.OldProperty,
            input.NewProperty,
            currentUserAccessor.GetCurrentUserName() ?? "unknown administrator",
            ct));
    }

    [HttpPost("{id}/Finish")]
    public async Task<ActionResult<MigrationDto>> Finish(string id, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        return await Run(() => migrationService.Finish(id, ct));
    }

    [HttpPost("{id}/Revert")]
    public async Task<ActionResult<MigrationDto>> Revert(string id, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        return await Run(() => migrationService.Revert(id, ct));
    }

    private async Task<ActionResult<MigrationDto>> Run(Func<Task<Migration>> action)
    {
        try
        {
            return Ok(MigrationDto.Create(await action()));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(exception.Message);
        }
    }
}