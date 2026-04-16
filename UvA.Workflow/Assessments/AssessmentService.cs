using UvA.Workflow.Submissions;

namespace UvA.Workflow.Assessments;

public static class AssessmentService
{
    public static Dictionary<string, Result[]> CalculateFormResults(SubmissionContext submissionContext,
        string? pageName)
    {
        var pages = submissionContext.Form.ActualForm.Pages.ToArray();

        int totalWeight = pages
            .SelectMany(page => page.Fields.Where(field => field.Weight.HasValue))
            .Sum(field => field.Weight ?? 0);

        return pages
            .Where(page => page.Fields.Any(field => field.Weight.HasValue)) // Filter out pages without a weight
            .Where(page => string.IsNullOrEmpty(pageName) || page.Name == pageName)
            .ToDictionary(
                page => page.Name,
                page => page.Fields.Where(field => field.Weight.HasValue) // Filter out fields without a weight
                    .Select(field =>
                    {
                        var answer =
                            submissionContext.Instance.GetProperty(submissionContext.Form.PropertyName, field.Name);

                        return new Result
                        {
                            QuestionName = field.Name,
                            Weight = field.Weight ?? 0,
                            Percentage = totalWeight == 0
                                ? 0
                                : (decimal)field.Weight.GetValueOrDefault() / totalWeight * 100,
                            Answer = answer is null || answer.IsBsonNull ? 0 : answer.ToDouble()
                        };
                    })
                    .ToArray()
            );
    }

    public static Dictionary<string, decimal> CalculateWeightedAverages(Dictionary<string, Result[]> results)
    {
        var output = new Dictionary<string, decimal>();
        var totalWeight = 0;
        decimal totalAnswersSum = 0;

        foreach (var (key, pageResults) in results)
        {
            int pageWeight = pageResults.Sum(r => r.Weight);
            decimal pageAnswersSum = pageResults.Sum(r => (decimal)r.Answer * r.Weight);
            totalWeight += pageWeight;
            totalAnswersSum += pageAnswersSum;

            output[key] = pageWeight == 0
                ? 0
                : Math.Round(pageAnswersSum / pageWeight, 2, MidpointRounding.AwayFromZero);
        }

        output["total"] = totalWeight == 0
            ? 0
            : Math.Round(totalAnswersSum / totalWeight, 2, MidpointRounding.AwayFromZero);

        return output;
    }
}