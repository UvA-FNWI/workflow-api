using System.Net;

namespace UvA.Workflow.Api.Exceptions;

public partial class ErrorCode
{
    public static readonly ErrorCode WorkflowInstancesNotFound = new(
        nameof(WorkflowInstancesNotFound),
        "Workflow instance not found",
        HttpStatusCode.NotFound
    );

    public static readonly ErrorCode WorkflowInstancesInvalidInput = new(
        nameof(WorkflowInstancesInvalidInput),
        "Invalid workflow instance input",
        HttpStatusCode.BadRequest
    );

    public static readonly ErrorCode WorkflowInstancesCreationFailed = new(
        nameof(WorkflowInstancesCreationFailed),
        "Failed to create workflow instance",
        HttpStatusCode.BadRequest
    );
}