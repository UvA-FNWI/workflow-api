namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record CreateWorkflowInstanceDto(
    string EntityType,
    string? ParentId = null,
    Dictionary<string, object>? InitialProperties = null
);