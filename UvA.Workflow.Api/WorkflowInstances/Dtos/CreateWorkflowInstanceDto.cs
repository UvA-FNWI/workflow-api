using System.Text.Json;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record CreateWorkflowInstanceDto(
    string EntityType,
    string? ParentId = null,
    Dictionary<string, JsonElement>? InitialProperties = null
);