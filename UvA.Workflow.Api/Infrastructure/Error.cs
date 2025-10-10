namespace UvA.Workflow.Api.Infrastructure;

/// <summary>
/// Standardized error response returned to clients
/// </summary>
public record Error(
    string ErrorCode,
    string Message,
    object? Details = null
);