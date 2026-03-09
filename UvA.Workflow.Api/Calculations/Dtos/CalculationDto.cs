using UvA.Workflow.Calculations;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Calculations.Dtos;

public record CalculationDto(
    string Id,
    Dictionary<string, Result[]> Results, // <Page name, Results for all questions of that page>
    Dictionary<string, double> WeightedAverages // <Page name, weighted average for all questions on that page>
)
{
    public static CalculationDto Create(SubmissionContext submissionContext)
    {
        var calculatedFormResults = CalculationService.CalculateFormResults(submissionContext);
        return new(
            submissionContext.Form.Name,
            calculatedFormResults,
            CalculationService.CalculateWeightedAverages(calculatedFormResults)
        );
    }
}