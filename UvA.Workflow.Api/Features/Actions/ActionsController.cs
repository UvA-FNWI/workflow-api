using UvA.Workflow.Api.Exceptions;
using UvA.Workflow.Api.Features.Actions.Dtos;

namespace UvA.Workflow.Api.Features.Actions;

[ApiController]
[Route("api/actions")]
public class ActionsController(
    InstanceService instanceService,
    RightsService rightsService,
    TriggerService triggerService,
    ContextService contextService) : ControllerBase
{
    [HttpPost("execute")]
    public async Task<ActionResult<ExecuteActionPayloadDto>> ExecuteAction([FromBody] ExecuteActionInputDto input)
    {
        var instance = await instanceService.Get(input.InstanceId);
        
        switch (input.Type)
        {
            case ActionType.DeleteInstance:
                if (!await rightsService.Can(instance, RoleAction.Submit))
                    return ErrorCode.ActionsNotPermitted;
                // TODO: delete it
                break;
            
            case ActionType.Execute:
                if (input.Name == null)
                    return ErrorCode.ActionsNameRequired;
                
                var actions = await rightsService.GetAllowedActions(instance, RoleAction.Execute);
                var action = actions.FirstOrDefault(a => a.Name == input.Name);
                if (action == null)
                    return ErrorCode.ActionsNotPermitted;
                
                await triggerService.RunTriggers(instance, action.Triggers, input.Mail);
                await contextService.UpdateCurrentStep(instance);
                break;
        }
        
        return Ok(new ExecuteActionPayloadDto(
            input.Type,
            input.Type == ActionType.DeleteInstance ? null : WorkflowInstanceDto.From(instance)
        ));
    }
}
