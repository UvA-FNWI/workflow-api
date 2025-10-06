using Uva.Workflow.Services;

namespace Uva.Workflow.Entities.Domain;

public enum ActionType
{
    SubmitForm,
    Execute,
    UndoState,
    CreateInstance,
    DeleteInstance
}

public record WorkflowAction(
    string InstanceId,
    ActionType Type,
    BilingualString Title,
    string? Form = null,
    string? Name = null,
    string? UserId = null,
    Mail? Mail = null,
    string? Property = null)
{
    public string Id => $"{InstanceId}_{Type}_{Name ?? Property ?? Form ?? UserId}";
}
