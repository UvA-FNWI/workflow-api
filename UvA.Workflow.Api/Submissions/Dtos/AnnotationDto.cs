using System.Text.Json;
using System.Text.Json.Nodes;

namespace UvA.Workflow.Api.Submissions.Dtos;

/// <summary>
/// DTO returned to the frontend for a single annotation.
/// Position is returned as raw JSON so react-pdf-highlighter can consume it without transformation.
/// </summary>
public record AnnotationDto(
    string Id,
    string HighlightedText,
    string Comment,
    JsonNode? Position
);

/// <summary>
/// DTO for creating a new annotation. Position is accepted as arbitrary JSON.
/// </summary>
public record CreateAnnotationDto(
    string HighlightedText,
    string Comment,
    JsonNode? Position
);