using System.Net;
using Microsoft.AspNetCore.Authorization;
using UvA.Workflow.Api.Authentication;

namespace UvA.Workflow.Api.Infrastructure;

[ApiController]
[Authorize(AuthenticationSchemes = WorkflowAuthenticationDefaults.UserScheme)]
[Route("[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult BadRequest(string code, string message, object? details = null) =>
        Error(HttpStatusCode.BadRequest, code, message, details);

    protected ObjectResult NotFound(string code, string message, object? details = null) =>
        Error(HttpStatusCode.NotFound, code, message, details);

    protected ObjectResult Conflict(string code, string message, object? details = null) =>
        Error(HttpStatusCode.Conflict, code, message, details);

    protected ObjectResult Unprocessable(string code, string message, object? details = null) =>
        Error(HttpStatusCode.UnprocessableEntity, code, message, details);

    protected ObjectResult Forbidden(object? details = null) =>
        Error(HttpStatusCode.Forbidden, "Forbidden", "Access forbidden", details);

    private ObjectResult Error(HttpStatusCode statusCode, string code, string message, object? details = null)
        => new(new Error(code, message, details))
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

    private const string ManualUserInternalEmailCode = "ManualUserInternalEmail";
    private const string ManualUserEmailAlreadyExistsCode = "ManualUserEmailAlreadyExists";
    private const string InvalidEmailAddressCode = "InvalidEmailAddress";
    private const string ExternalUsersNotAllowedCode = "ExternalUsersNotAllowed";
    private const string InvalidQuestionTypeCode = "InvalidQuestionType";

    protected ObjectResult MapExternalUserCreationError(ExternalUserCreationException ex) => ex.Reason switch
    {
        ExternalUserCreationFailureReason.InvalidEmailAddress =>
            BadRequest(InvalidEmailAddressCode, InvalidEmailAddressCode),
        ExternalUserCreationFailureReason.InternalEmailAddress =>
            BadRequest(ManualUserInternalEmailCode, ManualUserInternalEmailCode),
        ExternalUserCreationFailureReason.UserAlreadyExists =>
            Conflict(ManualUserEmailAlreadyExistsCode, ManualUserEmailAlreadyExistsCode),
        ExternalUserCreationFailureReason.ExternalUsersNotAllowed =>
            Unprocessable(ExternalUsersNotAllowedCode, ExternalUsersNotAllowedCode),
        ExternalUserCreationFailureReason.InvalidQuestionType =>
            Unprocessable(InvalidQuestionTypeCode, InvalidQuestionTypeCode),
        _ => Unprocessable(ex.Reason.ToString(), ex.Message)
    };
}