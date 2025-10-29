using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record SubmitSubmissionResult(
    SubmissionDto Submission,
    WorkflowInstanceDto? UpdatedInstance = null,
    InvalidQuestion[]? ValidationErrors = null,
    bool Success = true);