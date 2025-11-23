using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Infrastructure.Database;

namespace UvA.Workflow.Api.Infrastructure;

/// <summary>
/// Global exception handler that catches all unhandled exceptions and returns standardized error responses
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "An error occurred: {Message}", exception.Message);

        var (statusCode, code, message) = exception switch
        {
            EntityNotFoundException enf => (HttpStatusCode.NotFound, enf.Code, enf.Message),
            ForbiddenWorkflowActionException fwae => (HttpStatusCode.Forbidden, fwae.Code, fwae.Message),
            InvalidWorkflowStateException iwse => (HttpStatusCode.UnprocessableEntity, iwse.Code, iwse.Message),
            WorkflowException wfe => (HttpStatusCode.InternalServerError, wfe.Code, wfe.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, "NotFound", "Resource not found"),
            ArgumentException => (HttpStatusCode.BadRequest, "InvalidInput", "Invalid input provided"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized", "Unauthorized access"),
            _ => (HttpStatusCode.InternalServerError, "Unknown", "An unknown error occurred")
        };

        httpContext.Response.Clear();
        httpContext.Response.ContentType = "application/json";
        httpContext.Response.StatusCode = (int)statusCode;

        var errorResponse = new
        {
            error = code,
            message,
            traceId = httpContext.TraceIdentifier
        };

        await httpContext.Response.WriteAsJsonAsync(errorResponse, cancellationToken);

        return true;
    }
}