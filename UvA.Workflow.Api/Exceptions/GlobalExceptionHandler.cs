using Microsoft.AspNetCore.Diagnostics;
using System.Net;

namespace UvA.Workflow.Api.Exceptions;

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

        var (statusCode, errorResponse) = exception switch
        {
            KeyNotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                new ErrorResponse(
                    ErrorCode.GeneralNotFound.Code,
                    notFoundEx.Message ?? "Resource not found"
                )
            ),
            ArgumentException argEx => (
                HttpStatusCode.BadRequest,
                new ErrorResponse(
                    ErrorCode.GeneralInvalidInput.Code,
                    argEx.Message ?? "Invalid input"
                )
            ),
            UnauthorizedAccessException => (
                HttpStatusCode.Unauthorized,
                new ErrorResponse(
                    ErrorCode.GeneralUnauthorized.Code,
                    "Unauthorized access"
                )
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                new ErrorResponse(
                    ErrorCode.GeneralUnknown.Code,
                    "An unexpected error occurred"
                )
            )
        };

        httpContext.Response.StatusCode = (int)statusCode;
        await httpContext.Response.WriteAsJsonAsync(errorResponse, cancellationToken);

        return true;
    }
}