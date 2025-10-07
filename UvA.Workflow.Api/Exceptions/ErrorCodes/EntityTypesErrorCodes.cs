using System.Net;

namespace UvA.Workflow.Api.Exceptions;

public partial class ErrorCode
{
    public static readonly ErrorCode EntityTypesNotFound = new(
        nameof(EntityTypesNotFound),
        "Entity type not found",
        HttpStatusCode.NotFound
    );
}
