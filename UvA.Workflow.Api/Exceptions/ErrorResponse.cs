namespace UvA.Workflow.Api.Exceptions;

/// <summary>
/// Standardized error response returned to clients
/// </summary>
public record ErrorResponse(
    /// <summary>The error code that identifies the type of error</summary>
    string ErrorCode,
    /// <summary>Human-readable error message</summary>
    string Message,
    /// <summary>Optional additional details about the error</summary>
    object? Details = null
);