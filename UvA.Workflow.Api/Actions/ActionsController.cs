using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Api.WorkflowInstances.Dtos;

namespace UvA.Workflow.Api.Actions;

public class ActionsController(
    IWorkflowInstanceRepository workflowInstanceRepository,
    RightsService rightsService,
    TriggerService triggerService,
    ContextService contextService,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ExecuteActionPayloadDto>> ExecuteAction([FromBody] ExecuteActionInputDto input,
        CancellationToken ct)
    {
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

                await triggerService.RunTriggers(instance, action.Triggers, ct, input.Mail);
                await contextService.UpdateCurrentStep(instance, ct);
                break;
        }

        return Ok(new ExecuteActionPayloadDto(
            input.Type,
            input.Type == ActionType.DeleteInstance ? null : await workflowInstanceDtoFactory.Create(instance, ct)
        ));
    }
}