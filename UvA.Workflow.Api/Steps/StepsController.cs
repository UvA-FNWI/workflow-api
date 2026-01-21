using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Versioning;

namespace UvA.Workflow.Api.Steps;

public class StepsController(
    IUserService userService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    IStepVersionService stepVersionService
) : ApiControllerBase
{
    /// <summary>
    /// Gets all versions of a step with their submission history
    /// </summary>
    //[Authorize(AuthenticationSchemes = AuthenticationExtensions.AllSchemes)] TODO: enable again
    [HttpGet("/api/instances/{instanceId}/steps/{stepName}/versions")]
    public async Task<ActionResult<StepVersionsResponse>> GetStepVersions(
        string instanceId,
        string stepName,
        CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null)
            return Unauthorized();

        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            return NotFound();

        try
        {
            var versions = await stepVersionService.GetStepVersions(instance, stepName, ct);
            return Ok(versions);
        }
        catch (EntityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}