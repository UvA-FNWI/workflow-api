namespace UvA.Workflow.Infrastructure;

[Serializable]
public class WorkflowException(string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; private set; } = code;
    public object? Details { get; private set; } = details;
}

public class EntityNotFoundException(string entityType, string entityId)
    : WorkflowException($"{entityType}NotFound", $"Entity {entityType} {entityId} not found");

public class ForbiddenWorkflowActionException(string workflowInstanceId, RoleAction action, string? formId)
    : WorkflowException("Forbidden",
        $"User does not have permission to {action} workflow instance {workflowInstanceId} for form {formId ?? "all forms"}");

public class InvalidWorkflowStateException(string workflowInstanceId, string code, string message)
    : WorkflowException(code, message, new { workflowInstanceId });