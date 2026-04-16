using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Assessments;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments.Dtos;

public record AssessmentDto(
    string Id,
    BilingualString FormTitle,
    Dictionary<string, Result[]> Results, // <Page name, Results for all questions of that page>
    Dictionary<string, decimal> WeightedAverages, // <Page name, weighted average for all questions on that page>
    AnswerDto[] Answers
);

public record AssessmentGroupDto(
    string Id,
    AssessmentDto[] Forms
);

public class AssessmentDtoFactory(ArtifactTokenService artifactTokenService, ModelService modelService)
{
    private readonly AnswerDtoFactory _answerDtoFactory = new(artifactTokenService);

    public AssessmentDto Create(SubmissionContext submissionContext, string? pageName = null)
    {
        var results = AssessmentService.CalculateFormResults(submissionContext, pageName);
        var weightedAverages = AssessmentService.CalculateWeightedAverages(results);

        var shownQuestionIds =
            modelService.GetQuestionStatus(submissionContext.Instance, submissionContext.Form, true);
        var questionNamesInForm = submissionContext.Form.ActualForm.Pages
            .Where(p => string.IsNullOrEmpty(pageName) || p.Name == pageName)
            .SelectMany(p => p.Fields)
            .Select(f => f.Name);
        var answers = Answer.Create(submissionContext.Instance, submissionContext.Form, shownQuestionIds)
            .Where(a => questionNamesInForm.Contains(a.QuestionName)).ToArray();

        results.Values
            .SelectMany(arr => arr)
            .ForEach(r => r.Percentage = Math.Round(r.Percentage, 2, MidpointRounding.AwayFromZero));

        return new(
            submissionContext.Form.Name,
            submissionContext.Form.DisplayName,
            results,
            weightedAverages,
            answers.Select(a => _answerDtoFactory.Create(a)).ToArray()
        );
    }

    public AssessmentGroupDto CreateGroup(string id, IEnumerable<SubmissionContext> contexts,
        string? pageName = null)
        => new(
            id,
            contexts.Select(c => Create(c, pageName)).ToArray()
        );
}