using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Jobs;
using UvA.Workflow.Notifications;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Actions;

public class ActionsController(
    IWorkflowInstanceRepository workflowInstanceRepository,
    IUserService userService,
    RightsService rightsService,
    EffectService effectService,
    JobService jobService,
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

                var actions = await instanceService.GetAllowedActions(instance, ct);
                var action = actions.FirstOrDefault(a =>
                    a.Action.Type == RoleAction.Execute && a.Action.Name == input.Name)?.Action;
                if (action == null)
                    return Forbidden();

                // Always log execute events implicitly
                await effectService.AddEvent(instance, input.Name, realUser, ct);

                result = await jobService.CreateAndRunJob(instance, action, realUser, input.JobInput, ct);
                await instanceService.UpdateCurrentStep(instance, ct);
                break;
        }

        return Ok(new ExecuteActionPayloadDto(
            input.Type,
            input.Type == ActionType.DeleteInstance ? null : await workflowInstanceDtoFactory.Create(instance, ct),
            result
        ));
    }
}