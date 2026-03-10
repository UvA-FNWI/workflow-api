using UvA.Workflow.Assessments;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments.Dtos;

public record AssessmentDto(
    string Id,
    Dictionary<string, Result[]> Results, // <Page name, Results for all questions of that page>
    Dictionary<string, double> WeightedAverages // <Page name, weighted average for all questions on that page>
)
{
    public static AssessmentDto Create(SubmissionContext submissionContext)
    {
        var calculatedFormResults = AssessmentService.CalculateFormResults(submissionContext, null);
        return new(
            submissionContext.Form.Name,
            calculatedFormResults,
            AssessmentService.CalculateWeightedAverages(calculatedFormResults, null)
        );
    }
}

public record AssessmentPageDto(
    string Id,
    string FormName,
    Result[] Results, // Results for all questions of that page
    double WeightedAverage // weighted average for all questions on that page
)
{
    public static AssessmentPageDto Create(SubmissionContext submissionContext, string pageName)
    {
        var results = AssessmentService.CalculateFormResults(submissionContext, pageName);
        var weighted = AssessmentService.CalculateWeightedAverages(results, pageName);

        return new(
            pageName,
            submissionContext.Form.Name,
            results.GetValueOrDefault(pageName) ?? Array.Empty<Result>(),
            weighted.GetValueOrDefault(pageName)
        );
    }
}