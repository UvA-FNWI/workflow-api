using UvA.Workflow.Submissions;

namespace UvA.Workflow.Assessments;

public static class AssessmentService
{
    public static Dictionary<string, Result[]> CalculateFormResults(SubmissionContext submissionContext,
        string? pageName)
    {
        int totalWeight = submissionContext.Form.Pages
            .SelectMany(page => page.Fields.Where(field => field.Weight.HasValue))
            .Sum(field => field.Weight ?? 0);

        return submissionContext.Form.Pages
            .Where(page => page.Fields.Any(field => field.Weight.HasValue)) // Filter out pages without a weight
            .Where(page => string.IsNullOrEmpty(pageName) || page.Name == pageName)
            .ToDictionary(
                page => page.Name,
                page => page.Fields.Where(field => field.Weight.HasValue) // Filter out fields without a weight
                    .Select(field =>
                    {
                        var answer = submissionContext.Instance.Properties.SingleOrDefault(a => a.Key == field.Name);

                        var answerString = ObjectContext.GetValue(answer.Value, DataType.String, null) as string;
                        return new Result
                        {
                            QuestionName = field.Name,
                            Weight = field.Weight ?? 0,
                            Percentage = totalWeight == 0
                                ? 0
                                : Math.Round(((double)field.Weight / totalWeight) * 100, 2),
                            Answer = int.TryParse(answerString, out var n) ? n : 0
                        };
                    })
                    .ToArray()
            );
    }

    public static Dictionary<string, double> CalculateWeightedAverages(Dictionary<string, Result[]> results,
        string? pageName)
    {
        var output = new Dictionary<string, double>();
        var totalWeight = 0;
        double totalWeightedSum = 0;

        foreach (var (key, pageResults) in results)
        {
            int totalPageWeight = pageResults.Sum(r => r.Weight);
            double totalPageWeightedSum = pageResults.Sum(r => r.Answer * r.Weight);
            totalWeight += totalPageWeight;
            totalWeightedSum += totalPageWeightedSum;

            output[key] = totalPageWeight == 0
                ? 0
                : Math.Round(totalPageWeightedSum / totalPageWeight, 2);
        }


        output["total"] = totalWeight == 0 ? 0 : Math.Round(totalWeightedSum / totalWeight, 2);

        return output;
    }
}