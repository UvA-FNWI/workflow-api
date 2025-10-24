using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record SubmissionDto(
    string Id,
    string FormName,
    string InstanceId,
    Answer[] Answers,
    DateTime? DateSubmitted,
    FormDto Form)
{
    public static SubmissionDto Create(WorkflowInstance inst,
        Form form,
        InstanceEvent? sub,
        Dictionary<string, QuestionStatus>? shownQuestionIds = null
    )
        => new(form.Name,
            form.Name,
            inst.Id,
            shownQuestionIds == null ? [] : Answer.Create(inst, form, shownQuestionIds),
            sub?.Date,
            FormDto.Create(form)
        );
}



public record SubmitSubmissionResult(
    SubmissionDto Submission,
    WorkflowInstanceDto? UpdatedInstance = null,
    InvalidQuestion[]? ValidationErrors = null,
    bool Success = true);