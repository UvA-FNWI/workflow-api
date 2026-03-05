using UvA.Workflow.Submissions;

namespace UvA.Workflow.Calculations;

public static class CalculationService
{
    public static Result[] CalculateFormResults(Answer[] answers, Form form)
    {
        return form.Pages
            .SelectMany(page =>
                page.Fields
                    .Where(field => field.Weight.HasValue)
                    .Select(field =>
                    {
                        var answer = answers.FirstOrDefault(a => a.QuestionName == field.Name);

                        return new Result
                        {
                            QuestionName = field.Name,
                            PageName = page.Name,
                            Weight = field.Weight ?? 0,
                            Answer = int.TryParse(answer?.Value?.GetString(), out var n) ? n : 0
                        };
                    })
            )
            .ToArray();
    }

    public static Dictionary<string, double> CalculateWeightedAverages(Result[] results)
    {
        if (results.Length == 0)
            return new Dictionary<string, double>();

        var dict = results
            .GroupBy(r => r.PageName)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    double weightSum = g.Sum(r => r.Weight);
                    return weightSum == 0 ? 0 : Math.Round(g.Sum(r => r.Answer * r.Weight) / weightSum, 2);
                });

        // Total weighted average
        double totalWeight = results.Sum(r => r.Weight);
        double totalWeightedSum = results.Sum(r => r.Answer * r.Weight);

        dict["total"] = totalWeight == 0 ? 0 : Math.Round(totalWeightedSum / totalWeight, 2);

        return dict;
    }
}