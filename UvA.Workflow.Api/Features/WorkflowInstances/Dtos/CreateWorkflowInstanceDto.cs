namespace UvA.Workflow.Api.Features.WorkflowInstances.Dtos;

public record CreateWorkflowInstanceDto(
    string EntityType,
    string? ParentId = null,
    Dictionary<string, object>? InitialProperties = null
);