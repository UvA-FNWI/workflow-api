using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Migrations;

namespace UvA.Workflow.Api.Migrations;

[Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.AnyScheme)]
public class MigrationsController(
    MigrationService migrationService,
    RightsService rightsService,
    ILogger<MigrationsController> logger) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MigrationDto>>> Get(CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);
        try
        {
            return Ok((await migrationService.GetAll(ct)).Select(MigrationDto.Create).ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Error loading migrations");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "MigrationLoadFailed",
                message = exception.Message,
                traceId = HttpContext.TraceIdentifier
            });
        }
    }
}