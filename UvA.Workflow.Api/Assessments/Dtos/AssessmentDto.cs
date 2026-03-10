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
        var calculatedFormResults = AssessmentService.CalculateFormResults(submissionContext);
        return new(
            submissionContext.Form.Name,
            calculatedFormResults,
            AssessmentService.CalculateWeightedAverages(calculatedFormResults)
        );
    }
}