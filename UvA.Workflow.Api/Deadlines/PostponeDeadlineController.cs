using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Deadlines;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Deadlines;

public record PostponeDeadlinesResponseDto(WorkflowInstanceDto Instance, EffectResult? Effects);

public class PostponeDeadlineController(
    IWorkflowInstanceRepository repository,
    IUserService userService,
    RightsService rightsService,
    PostponeDeadlineService postponeDeadlineService,
    WorkflowInstanceDtoFactory dtoFactory) : ApiControllerBase
{
    [HttpPost("{instanceId}/{actionName}")]
    public async Task<ActionResult<PostponeDeadlinesResponseDto>> Postpone(string instanceId, string actionName,
        [FromBody] PostponeDeadlinesRequest request, CancellationToken ct)
    {
        if (await userService.GetCurrentUser(ct) == null) return Unauthorized();
        var instance = await repository.GetById(instanceId, ct);
        if (instance == null) return WorkflowInstanceNotFound;

        var action = (await rightsService.GetAllowedActions(instance, RoleAction.PostponeDeadlines))
            .FirstOrDefault(action => action.Name == actionName && action.Steps.Length == 0);
        if (action?.Form == null)
            return Forbidden();

        var user = await userService.GetRealUser(ct);
        if (user == null) return Unauthorized();
        var result = await postponeDeadlineService.Postpone(instance, action, request, user, ct);
        if (result.Errors.Length != 0) return UnprocessableEntity(result.Errors);
        return Ok(new PostponeDeadlinesResponseDto(await dtoFactory.Create(instance, ct), result.Effects));
    }
}