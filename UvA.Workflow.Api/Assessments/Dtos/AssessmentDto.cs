using UvA.Workflow.Assessments;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments.Dtos;

public record AssessmentDto(
    string Id,
    Dictionary<string, Result[]> Results, // <Page name, Results for all questions of that page>
    Dictionary<string, decimal> WeightedAverages // <Page name, weighted average for all questions on that page>
)
{
    public static AssessmentDto Create(SubmissionContext submissionContext)
    {
        var results = AssessmentService.CalculateFormResults(submissionContext, null);
        var weightedAverages = AssessmentService.CalculateWeightedAverages(results, null);

        results.Values
            .SelectMany(arr => arr)
            .ForEach(r => r.Percentage = Math.Round(r.Percentage, 2, MidpointRounding.AwayFromZero));

        return new(
            submissionContext.Form.Name,
            results,
            weightedAverages
        );
    }
}

public record AssessmentPageDto(
    string Id,
    string FormName,
    Result[] Results, // Results for all questions of that page
    decimal WeightedAverage // weighted average for all questions on that page
)
{
    public static AssessmentPageDto Create(SubmissionContext submissionContext, string pageName)
    {
        var results = AssessmentService.CalculateFormResults(submissionContext, pageName);
        var weightedAverages = AssessmentService.CalculateWeightedAverages(results, pageName);

        results.Values
            .SelectMany(arr => arr)
            .ForEach(r => r.Percentage = Math.Round(r.Percentage, 2, MidpointRounding.AwayFromZero));

        return new(
            pageName,
            submissionContext.Form.Name,
            results.GetValueOrDefault(pageName) ?? Array.Empty<Result>(),
            weightedAverages.GetValueOrDefault(pageName)
        );
    }
}