using System.Net;

namespace UvA.Workflow.Api.Exceptions;

public partial class ErrorCode
{
    public static readonly ErrorCode SubmissionsInstanceNotFound = new(
        nameof(SubmissionsInstanceNotFound),
        "Workflow instance not found",
        HttpStatusCode.NotFound
    );

    public static readonly ErrorCode SubmissionsAlreadySubmitted = new(
        nameof(SubmissionsAlreadySubmitted),
        "Submission already submitted",
        HttpStatusCode.BadRequest
    );

    public static readonly ErrorCode SubmissionsValidationFailed = new(
        nameof(SubmissionsValidationFailed),
        "Submission validation failed",
        HttpStatusCode.BadRequest
    );

    public static readonly ErrorCode SubmissionsQuestionNotFound = new(
        nameof(SubmissionsQuestionNotFound),
        "Question not found",
        HttpStatusCode.NotFound
    );

    public static readonly ErrorCode SubmissionsUnauthorized = new(
        nameof(SubmissionsUnauthorized),
        "Not authorized to modify submission",
        HttpStatusCode.Forbidden
    );

    public static readonly ErrorCode SubmissionsSaveAnswerFailed = new(
        nameof(SubmissionsSaveAnswerFailed),
        "Failed to save answer",
        HttpStatusCode.BadRequest
    );
}
