using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.Events;

[Route("/WorkflowInstances/{instanceId}/Events")]
public class EventsController(
    IWorkflowInstanceRepository workflowRepository,
    IUserService userService,
    InstanceService instanceService)
    : ApiControllerBase
{
    [HttpDelete]
    [Route("{eventName}")]
    public async Task<IActionResult> DeleteEvent(string instanceId, string eventName, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null)
            return Unauthorized();

        var instance = await workflowRepository.GetById(instanceId, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;
        await instanceService.DeleteEvent(instance, eventName, user, ct);
        return Ok();
    }
}