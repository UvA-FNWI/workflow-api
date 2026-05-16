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

    private static decimal WeightedAverage(IEnumerable<Result> results)
    {
        var list = results.ToList();
        int totalWeight = list.Sum(r => r.Weight);
        decimal weightedSum = list.Sum(r => (decimal)r.Answer * r.Weight);

        return totalWeight == 0
            ? 0
            : Math.Round(weightedSum / totalWeight, 2, MidpointRounding.AwayFromZero);
    }


    public static Dictionary<string, decimal> CalculateWeightedAverages(Dictionary<string, Result[]> results) =>
        results.ToDictionary(kvp => kvp.Key, kvp => WeightedAverage(kvp.Value));


    public static decimal CalculateTotalWeightedAverage(IEnumerable<Dictionary<string, Result[]>> formResults)
    {
        // Materialize once so the collection can be safely iterated multiple times
        var forms = formResults.ToList();
        var allPageNames = forms.SelectMany(f => f.Keys).Distinct();

        // For each page, compute the average weighted average across all forms that filled it.
        // A page is considered filled if at least one of its answers is non-zero.
        // If no form filled a page, the aggregate average is null — used later to return 0.
        var pageAggregates = allPageNames.Select(page =>
        {
            var filledPages = forms
                .Where(f => f.TryGetValue(page, out var pageResults) && pageResults.Any(r => r.Answer != 0))
                .Select(f => f[page])
                .ToList();
            if (filledPages.Count == 0)
                return (Average: null, Weight: 0);

            var weight = filledPages[0].Sum(r => r.Weight);
            var averageAnswer = filledPages.Average(WeightedAverage);

            return (Average: (decimal?)averageAnswer, Weight: weight);
        }).ToList();

        // Only return a meaningful total if every page was filled in by at least one form
        if (pageAggregates.Any(p => p.Average == null)) return 0;

        int totalWeight = pageAggregates.Sum(r => r.Weight);
        decimal weightedSum = pageAggregates.Sum(p => p.Average!.Value * p.Weight);
        return totalWeight == 0 ? 0 : Math.Round(weightedSum / totalWeight, 2, MidpointRounding.AwayFromZero);
    }
}