using System.Text.Json;
using UvA.Workflow.Api.Submissions.Dtos;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

/// <summary>
/// Properties and values exposed by the admin data view.
/// </summary>
/// <remarks>
/// <c>Values</c> uses dotted property paths. Null means the value is unset or incompatible with its current
/// definition. Embedded objects expose child paths; object arrays remain whole.
/// </remarks>
public record InstancePropertiesDto(
    QuestionDto[] Properties,
    Dictionary<string, JsonElement?> Values);

public record SaveInstancePropertyRequest(JsonElement? Value);