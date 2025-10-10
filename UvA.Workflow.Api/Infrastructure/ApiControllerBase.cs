using System.Net;

namespace UvA.Workflow.Api.Infrastructure;

[ApiController]
[Route("[controller]/[action]")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult BadRequest(string code, string message, object? details = null) =>
        Error(HttpStatusCode.BadRequest,code, message, details);
    
    protected ObjectResult NotFound(string code, string message, object? details = null) =>
        Error(HttpStatusCode.NotFound,code, message, details);
    
    protected ObjectResult Conflict(string code, string message, object? details = null) =>
       Error(HttpStatusCode.Conflict,code, message, details);
    
    protected ObjectResult Unprocessable(string code, string message, object? details = null) =>
       Error(HttpStatusCode.UnprocessableEntity,code, message, details);
    
    protected ObjectResult Forbidden(object? details = null) => 
        Error(HttpStatusCode.Forbidden,"Forbidden", "Access forbidden", details);

    private ObjectResult Error(HttpStatusCode statusCode, string code, string message, object? details = null)
     =>new(new Error(code, message, details))
        {
            StatusCode = (int)statusCode
        };
    
    /// <summary>
    /// Some reusable error results 
    /// </summary>
    protected ObjectResult WorkflowInstanceNotFound =>
        NotFound("WorkflowInstanceNotFound", "Workflow instance not found");
    
    protected ObjectResult UserNotFound =>
        NotFound("UserNotFound", "User not found");
}