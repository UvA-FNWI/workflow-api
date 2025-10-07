using System.Net;

namespace UvA.Workflow.Api.Exceptions;

public partial class ErrorCode
{
    public static readonly ErrorCode UsersNotFound = new(
        nameof(UsersNotFound),
        "User not found",
        HttpStatusCode.NotFound
    );

    public static readonly ErrorCode UsersInvalidInput = new(
        nameof(UsersInvalidInput),
        "Invalid user input",
        HttpStatusCode.BadRequest
    );

    public static readonly ErrorCode UsersAlreadyExists = new(
        nameof(UsersAlreadyExists),
        "User already exists",
        HttpStatusCode.Conflict
    );
}
