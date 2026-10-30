using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Actions;

public class ActionsController(
    IWorkflowInstanceRepository workflowInstanceRepository,
    IUserService userService,
    RightsService rightsService,
    ActionExecutionService actionExecutionService,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory
) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ExecuteActionPayloadDto>> ExecuteAction([FromBody] ExecuteActionInputDto input,
        CancellationToken ct)
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

        var result = new EffectResult();

        switch (input.Type)
        {
            case ActionType.DeleteInstance:
                if (!await rightsService.Can(instance, RoleAction.Delete))
                    return Forbidden();
                // TODO: delete it
                break;

            case ActionType.Execute:
                if (input.Name == null)
                    return BadRequest("ActionNameRequired", "Action name is required");

                var actions = await rightsService.GetAllowedActions(instance, RoleAction.Execute);
                var action = actions.FirstOrDefault(a => a.Name == input.Name);
                if (action == null)
                    return Forbidden();

                result = await actionExecutionService.Execute(instance, action, realUser, input.JobInput, ct);
                break;
        }

        return Ok(new ExecuteActionPayloadDto(
            input.Type,
            input.Type == ActionType.DeleteInstance ? null : await workflowInstanceDtoFactory.Create(instance, ct),
            result
        ));
    }
}