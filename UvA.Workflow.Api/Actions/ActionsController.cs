using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;

namespace UvA.Workflow.Api.Actions;

public class ActionsController(
    IWorkflowInstanceRepository workflowInstanceRepository,
    IUserService userService,
    RightsService rightsService,
    EffectService effectService,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory,
    InstanceService instanceService
) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ExecuteActionPayloadDto>> ExecuteAction([FromBody] ExecuteActionInputDto input,
        CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var instance = await workflowInstanceRepository.GetById(input.InstanceId, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;

        switch (input.Type)
        {
            case ActionType.DeleteInstance:
                if (!await rightsService.Can(instance, RoleAction.Submit))
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

                await effectService.RunEffects(instance, action.OnAction, currentUser, ct, input.Mail);
                await instanceService.UpdateCurrentStep(instance, ct);
                break;
        }

        return Ok(new ExecuteActionPayloadDto(
            input.Type,
            input.Type == ActionType.DeleteInstance ? null : await workflowInstanceDtoFactory.Create(instance, ct)
        ));
    }
}