using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Actions;

public class ActionsController(
    IWorkflowInstanceRepository workflowInstanceRepository,
    IUserService userService,
    RightsService rightsService,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory,
    FormDtoFactory formDtoFactory,
    IEnumerable<IActionHandler> actionHandlers
) : ApiControllerBase
{
    [HttpGet("{instanceId}/{actionName}/Form")]
    public async Task<ActionResult<FormDto>> GetForm(string instanceId, string actionName, CancellationToken ct)
    {
        if (await userService.GetCurrentUser(ct) == null)
            return Unauthorized();

        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        var action = (await rightsService.GetAllowedActions(
                instance, RoleAction.Execute, RoleAction.PostponeDeadlines))
            .FirstOrDefault(action => action.Name == actionName && action.Form != null &&
                                      (action.Type != RoleAction.PostponeDeadlines || action.Steps.Length == 0));
        if (action?.Form == null)
            return Forbidden();

        return Ok(await formDtoFactory.Create(instance, action.Form, ct));
    }

    [HttpPost]
    public async Task<ActionResult<ExecuteActionPayloadDto>> ExecuteAction(
        [FromBody] ExecuteActionInputDto input, CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var realUser = await userService.GetRealUser(ct);
        if (realUser == null)
            throw new Exception("Could not resolve real user");

        var instance = await workflowInstanceRepository.GetById(input.InstanceId, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        var handler = actionHandlers.FirstOrDefault(handler =>
                          handler.Type == input.Type && handler.ActionName == input.Name)
                      ?? actionHandlers.FirstOrDefault(handler =>
                          handler.Type == input.Type && handler.ActionName == null);
        if (handler != null)
        {
            var result = await handler.Execute(instance, realUser, input, ct);
            if (result.Error is { } error)
                return MapError(error);

            return Ok(new ExecuteActionPayloadDto(
                input.Type,
                await workflowInstanceDtoFactory.Create(instance, ct),
                result.Effects ?? new EffectResult()));
        }

        if (input.Type == ActionType.DeleteInstance)
        {
            if (!await rightsService.Can(instance, RoleAction.Delete))
                return Forbidden();
            // TODO: delete it
            return Ok(new ExecuteActionPayloadDto(input.Type, null, new EffectResult()));
        }

        return BadRequest("UnsupportedActionType", $"Action type '{input.Type}' is not supported");
    }

    private ObjectResult MapError(ActionHandlerError error) => error.Type switch
    {
        ActionHandlerErrorType.BadRequest => BadRequest(error.Code, error.Message, error.Details),
        ActionHandlerErrorType.Forbidden => Forbidden(error.Details),
        ActionHandlerErrorType.UnprocessableEntity => error.Details == null
            ? Unprocessable(error.Code, error.Message)
            : UnprocessableEntity(error.Details),
        _ => throw new ArgumentOutOfRangeException()
    };
}