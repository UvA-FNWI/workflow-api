using System.Net;

namespace UvA.Workflow.Api.Exceptions;

public partial class ErrorCode
{
    public static readonly ErrorCode ActionsNameRequired = new(
        nameof(ActionsNameRequired),
        "Action name is required",
        HttpStatusCode.BadRequest
    );
}
