using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Events;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record SubmissionDto(
    string Id,
    string FormName,
    string InstanceId,
    AnswerDto[] Answers,
    DateTime? DateSubmitted,
    FormDto Form,
    RoleAction[] Permissions);

public class SubmissionDtoFactory(ArtifactTokenService artifactTokenService, ModelService modelService)
{
    private readonly AnswerDtoFactory _answerDtoFactory = new(artifactTokenService);

    public SubmissionDto Create(WorkflowInstance inst, Form form, InstanceEvent? submission,
        Dictionary<string, QuestionStatus>? shownQuestionIds = null, RoleAction[]? permissions = null)
    {
        var context = modelService.CreateContext(inst);
        var answers = shownQuestionIds == null ? [] : Answer.Create(inst, form, shownQuestionIds);
        return new(form.Name,
            form.Name,
            inst.Id,
            answers.Select(a => _answerDtoFactory.Create(a)).ToArray(),
            submission?.Date,
            FormDto.Create(form, context),
            permissions ?? []
        );
    }
}