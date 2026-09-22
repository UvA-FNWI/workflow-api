using System.Text.Json;
using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.Deadlines;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Actions;

public class PostponeDeadlinesActionHandler(
    RightsService rightsService,
    PostponeDeadlineService postponeDeadlineService) : IActionHandler
{
    public ActionType Type => ActionType.PostponeDeadlines;
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

        var action = (await rightsService.GetAllowedActions(instance, RoleAction.PostponeDeadlines))
            .FirstOrDefault(action => action.Name == input.Name && action.Steps.Length == 0);
        if (action?.Form == null)
            return new(Error: new(ActionHandlerErrorType.Forbidden, "Forbidden", "Access forbidden"));

        if (input.Input is not { } inputElement)
            return new(Error: new(ActionHandlerErrorType.BadRequest,
                "ActionInputRequired", "Action input is required"));

        PostponeDeadlinesRequest? request;
        try
        {
            request = inputElement.Deserialize<PostponeDeadlinesRequest>();
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request == null)
            return new(Error: new(ActionHandlerErrorType.BadRequest,
                "InvalidActionInput", "Invalid action input"));

        var result = await postponeDeadlineService.Postpone(instance, action, request, user, ct);
        return result.Errors.Length == 0
            ? new(result.Effects ?? new EffectResult())
            : new(Error: new(ActionHandlerErrorType.UnprocessableEntity,
                "PostponementFailed", "The deadlines could not be postponed", result.Errors));
    }
}