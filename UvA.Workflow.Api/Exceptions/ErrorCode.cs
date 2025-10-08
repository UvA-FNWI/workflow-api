using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace UvA.Workflow.Api.Exceptions;

/// <summary>
/// Represents an error code with its associated message and HTTP status code.
/// This class uses partial classes to organize error codes by feature in separate files.
/// Can be returned directly from controller actions.
/// </summary>
public partial class ErrorCode : ActionResult
{
    /// <summary>
    /// Gets the unique key for this error code (used by frontend)
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the error message
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the HTTP status code to return for this error
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    public ErrorCode(string code, string message, HttpStatusCode statusCode)
    {
        Code = code;
        Message = message;
        StatusCode = statusCode;
    }

    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = new ErrorResponse(Code, Message);
        context.HttpContext.Response.StatusCode = (int)StatusCode;
        await context.HttpContext.Response.WriteAsJsonAsync(response);
    }

    public override string ToString() => Code;
}