using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Jobs;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Actions;

public class ExecuteActionHandler(
    RightsService rightsService,
    EffectService effectService,
    JobService jobService,
    InstanceService instanceService) : IActionHandler
{
    public ActionType Type => ActionType.Execute;
    public string? ActionName => null;

    public async Task<ActionHandlerResult> Execute(
        WorkflowInstance instance,
        User user,
        ExecuteActionInputDto input,
        CancellationToken ct)
    {
        if (input.Name == null)
            return new(Error: new(ActionHandlerErrorType.BadRequest,
                "ActionNameRequired", "Action name is required"));

        var action = (await rightsService.GetAllowedActions(instance, RoleAction.Execute))
            .FirstOrDefault(action => action.Name == input.Name);
        if (action == null)
            return new(Error: new(ActionHandlerErrorType.Forbidden, "Forbidden", "Access forbidden"));

        if (action.Form != null)
            return new(Error: new(ActionHandlerErrorType.BadRequest,
                "DedicatedEndpointRequired", "Actions with a form must use their dedicated endpoint"));

        // Always log execute events implicitly.
        await effectService.AddEvent(instance, input.Name, user, ct);
        var effects = await jobService.CreateAndRunJob(instance, action, user, input.JobInput, ct);
        await instanceService.UpdateCurrentStep(instance, ct);
        return new(effects);
    }
}