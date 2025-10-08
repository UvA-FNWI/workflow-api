using System.Net;

namespace UvA.Workflow.Api.Exceptions;

public partial class ErrorCode
{
    public static readonly ErrorCode EntityTypeNotFound = new(
        nameof(EntityTypeNotFound),
        "Entity type not found",
        HttpStatusCode.NotFound
    );
}