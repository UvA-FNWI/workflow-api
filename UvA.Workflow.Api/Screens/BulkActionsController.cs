using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Screens.Dtos;

namespace UvA.Workflow.Api.Screens;

public class BulkActionsController(BulkActionsService bulkActionsService, IUserService userService) : ApiControllerBase
{
    [HttpPost("/Screens/{workflowDefinition}/{screenName}/BulkActions/{actionName}/Preview")]
    public async Task<ActionResult<BulkActionResultDto>> Preview(string workflowDefinition, string screenName,
        string actionName, BulkActionInputDto input, CancellationToken ct)
    {
        if (await userService.GetCurrentUser(ct) == null)
            return Unauthorized();
        if (input.InstanceIds.Any(string.IsNullOrWhiteSpace))
            return BadRequest("InvalidInstanceIds", "Instance IDs must not be empty.");
        try
        {
            return Ok(await bulkActionsService.Preview(workflowDefinition, screenName, actionName, input.InstanceIds,
                ct));
        }
        catch (ArgumentException ex)
        {
            return NotFound("BulkActionNotFound", ex.Message);
        }
    }

    [HttpPost("/Screens/{workflowDefinition}/{screenName}/BulkActions/{actionName}")]
    public async Task<ActionResult<BulkActionResultDto>> Execute(string workflowDefinition, string screenName,
        string actionName, BulkActionInputDto input, CancellationToken ct)
    {
        if (await userService.GetCurrentUser(ct) == null)
            return Unauthorized();
        if (input.InstanceIds.Any(string.IsNullOrWhiteSpace))
            return BadRequest("InvalidInstanceIds", "Instance IDs must not be empty.");
        var realUser = await userService.GetRealUser(ct)
                       ?? throw new InvalidOperationException("Could not resolve real user");
        try
        {
            return Ok(await bulkActionsService.Execute(workflowDefinition, screenName, actionName, input.InstanceIds,
                realUser, ct));
        }
        catch (ArgumentException ex)
        {
            return NotFound("BulkActionNotFound", ex.Message);
        }
    }
}