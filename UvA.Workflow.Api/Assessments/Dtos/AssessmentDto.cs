using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Assessments;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments.Dtos;

public record AssessmentPartDto(
    string Id,
    BilingualString SourceTitle,
    SourceResult SourceResults,
    AnswerDto[] Answers
);

public record AssessmentDto(
    string Id,
    AssessmentPartDto[] Parts,
    decimal? FinalGrade
);

public class AssessmentDtoFactory(ArtifactTokenService artifactTokenService, ModelService modelService)
{
    private readonly AnswerDtoFactory _answerDtoFactory = new(artifactTokenService);

    public AssessmentPartDto Create(SubmissionContext submissionContext, string? pageName = null)
    {
        var results = AssessmentService.CalculateSourceResults(submissionContext, pageName);

        var shownQuestionIds =
            modelService.GetQuestionStatus(submissionContext.Instance, submissionContext.Form, true);
        var questionNamesInForm = submissionContext.Form.ActualForm.Pages
            .Where(p => string.IsNullOrEmpty(pageName) || p.Name == pageName)
            .SelectMany(p => p.Fields)
            .Select(f => f.Name);
        var answers = Answer.Create(submissionContext.Instance, submissionContext.Form, shownQuestionIds)
            .Where(a => questionNamesInForm.Contains(a.QuestionName)).ToArray();

        return new(
            submissionContext.Form.Name,
            submissionContext.Form.DisplayName,
            results,
            answers.Select(a => _answerDtoFactory.Create(a)).ToArray()
        );
    }

    public AssessmentDto Create(string id, IEnumerable<SubmissionContext> contexts,
        AssessmentConfiguration? assessmentConfig = null,
        string? pageName = null)
    {
        var parts = contexts.Select(c => Create(c, pageName)).ToArray();
        var sourceResults = parts.Select(p => p.SourceResults);

        decimal? finalGrade = pageName != null ? null :
            assessmentConfig != null ? AssessmentService.CalculateFinalGrade(assessmentConfig, sourceResults) :
            AssessmentService.CalculateTotalWeightedAverage(sourceResults);

        return new(
            id,
            parts,
            pageName == null
                ? AssessmentService.CalculateTotalWeightedAverage(parts.Select(f => f.SourceResults))
                : null
        );
    }
}