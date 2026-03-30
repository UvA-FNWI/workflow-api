using UvA.Workflow.Assessments;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments.Dtos;

public record AssessmentDto(
    string Id,
    BilingualString FormTitle,
    Dictionary<string, Result[]> Results, // <Page name, Results for all questions of that page>
    Dictionary<string, decimal> WeightedAverages // <Page name, weighted average for all questions on that page>
)
{
    public static AssessmentDto Create(SubmissionContext submissionContext, string? pageName = null)
    {
        var results = AssessmentService.CalculateFormResults(submissionContext, pageName);
        var weightedAverages = AssessmentService.CalculateWeightedAverages(results);

        results.Values
            .SelectMany(arr => arr)
            .ForEach(r => r.Percentage = Math.Round(r.Percentage, 2, MidpointRounding.AwayFromZero));

        return new(
            submissionContext.Form.Name,
            submissionContext.Form.DisplayName,
            results,
            weightedAverages
        );
    }
}

public record AssessmentGroupDto(
    string Id,
    AssessmentDto[] Forms)
{
    public static AssessmentGroupDto Create(string id, IEnumerable<SubmissionContext> contexts,
        string? pageName = null)
        => new(
            id,
            contexts.Select(c => AssessmentDto.Create(c, pageName)).ToArray()
        );
}