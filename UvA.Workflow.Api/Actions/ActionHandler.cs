using UvA.Workflow.Api.Actions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Actions;

public interface IActionHandler
{
    ActionType Type { get; }
    string? ActionName { get; }

    Task<ActionHandlerResult> Execute(
        WorkflowInstance instance,
        User user,
        ExecuteActionInputDto input,
        CancellationToken ct);
}

public enum ActionHandlerErrorType
{
    BadRequest,
    Forbidden,
    UnprocessableEntity
}

public record ActionHandlerError(
    ActionHandlerErrorType Type,
    string Code,
    string Message,
    object? Details = null);

public record ActionHandlerResult(
    EffectResult? Effects = null,
    ActionHandlerError? Error = null);