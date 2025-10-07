using System.Net;

namespace UvA.Workflow.Api.Exceptions;

public partial class ErrorCode
{
    public static readonly ErrorCode ActionsNameRequired = new(
        nameof(ActionsNameRequired),
        "Action name is required",
        HttpStatusCode.BadRequest
    );

    public static readonly ErrorCode ActionsNotPermitted = new(
        nameof(ActionsNotPermitted),
        "Action not permitted",
        HttpStatusCode.Forbidden
    );

    public static readonly ErrorCode ActionsInstanceNotFound = new(
        nameof(ActionsInstanceNotFound),
        "Workflow instance not found",
        HttpStatusCode.NotFound
    );
}
