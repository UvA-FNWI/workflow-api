using System.Text.Json;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record CreateWorkflowInstanceDto(
    string WorkflowDefinition,
    Dictionary<string, JsonElement>? InitialProperties = null,
    Dictionary<string, DateTime>? InitialEvents = null);