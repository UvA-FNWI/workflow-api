using System.Net;

namespace UvA.Workflow.Api.Exceptions;

public partial class ErrorCode
{
    public static readonly ErrorCode GeneralUnknown = new(
        nameof(GeneralUnknown),
        "An unknown error occurred",
        HttpStatusCode.InternalServerError
    );

    public static readonly ErrorCode GeneralNotFound = new(
        nameof(GeneralNotFound),
        "Resource not found",
        HttpStatusCode.NotFound
    );

    public static readonly ErrorCode GeneralInvalidInput = new(
        nameof(GeneralInvalidInput),
        "Invalid input provided",
        HttpStatusCode.BadRequest
    );

    public static readonly ErrorCode GeneralUnauthorized = new(
        nameof(GeneralUnauthorized),
        "Unauthorized access",
        HttpStatusCode.Unauthorized
    );

    public static readonly ErrorCode GeneralForbidden = new(
        nameof(GeneralForbidden),
        "Access forbidden",
        HttpStatusCode.Forbidden
    );
}
