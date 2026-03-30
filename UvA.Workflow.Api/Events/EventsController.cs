using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Events;
using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Api.Events;

[Route("/WorkflowInstances/{instanceId}/Events")]
public class EventsController(
    IWorkflowInstanceRepository workflowRepository,
    IUserService userService,
    RightsService rightsService,
    IInstanceEventService eventService)
    : ApiControllerBase
{
    [HttpDelete]
    [Route("{eventName}")]
    public async Task<IActionResult> DeleteEvent(string instanceId, string eventName, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct) ??
                   throw new UnauthorizedAccessException();
        var instance = await workflowRepository.GetById(instanceId, ct) ??
                       throw new WorkflowInstanceNotFoundException(instanceId);

        await rightsService.EnsureAuthorizedForAction(instance, RoleAction.ViewAdminTools);

        await eventService.DeleteEvent(instance, eventName, user, ct);
        return Ok();
    }
}