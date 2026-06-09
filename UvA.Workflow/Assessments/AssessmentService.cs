using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Assessments;

public static class AssessmentService
{
    public static decimal CalculateFinalGrade(AssessmentConfiguration config, IEnumerable<SourceResult> sourceResults)
    {
        var totalPartWeight = config.Parts.Sum(p => p.Weight);
        if (totalPartWeight <= 0) return 0;

        decimal weightedSum = 0;
        foreach (var part in config.Parts)
        {
            var partScore = CalculatePartScore(part, sourceResults);
            if (partScore == 0) return 0;
            weightedSum += partScore * part.Weight;
        }

        return Math.Round(weightedSum / totalPartWeight, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Calculates the score for a single assessment part by combining its sources
    /// by their relative weights. Skips sources that haven't submitted yet.
    /// </summary>
    public static decimal CalculatePartScore(
        AssessmentPart part,
        IEnumerable<SourceResult> sourceResults)
    {
        var sourceList = sourceResults.ToList();
        decimal totalSourceWeight = 0;
        decimal weightedSum = 0;

        foreach (var sourceConfig in part.Sources)
        {
            var sourceResult = sourceList.FirstOrDefault(s => s.SourceName == sourceConfig.Name);
            if (sourceResult == null || sourceResult.Score == 0) continue;

            weightedSum += sourceResult.Score * sourceConfig.Weight;
            totalSourceWeight += sourceConfig.Weight;
        }

        return totalSourceWeight == 0
            ? 0
            : Math.Round(weightedSum / totalSourceWeight, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Calculates the total weighted average score for every page in a form
    /// </summary>
    public static SourceResult CalculateSourceResults(SubmissionContext submissionContext,
        string? pageName)
    {
        var pages = submissionContext.Form.ActualForm.Pages.ToArray();

        decimal totalWeight = pages
            .SelectMany(page => page.Fields.Where(field => field.Weight.HasValue))
            .Sum(field => field.Weight ?? 0);

        var pageResults = pages
            .Where(page => page.Fields.Any(field => field.Weight.HasValue)) // Filter out pages without a weight
            .Where(page => string.IsNullOrEmpty(pageName) || page.Name == pageName)
            .Select(page =>
            {
                var questions = page.Fields
                    .Where(field => field.Weight.HasValue)
                    .Select(field =>
                    {
                        var answerKey =
                            submissionContext.Instance.GetProperty(submissionContext.Form.PropertyName, field.Name);
                        return new QuestionResult
                        {
                            QuestionName = field.Name,
                            Weight = field.Weight ?? 0,
                            Percentage = totalWeight == 0
                                ? 0
                                : (decimal)field.Weight.GetValueOrDefault() / totalWeight * 100,
                            Answer = answerKey is null || answerKey.IsBsonNull
                                ? 0
                                : field.Values?.FirstOrDefault(v => v.Name == answerKey.AsString)?.Value ??
                                  answerKey.ToDouble()
                        };
                    }).ToList();

                return new PageResult
                {
                    PageName = page.Name,
                    Weight = questions.Sum(q => q.Weight),
                    WeightedAverage = CalculatePageWeightedAverage(questions),
                    QuestionResults = questions
                };
            }).ToList();
        return new SourceResult
        {
            SourceName = submissionContext.Form.PropertyName ?? "",
            Score = CalculateSourceScore(pageResults),
            PageResults = pageResults
        };
    }

    private static decimal CalculatePageWeightedAverage(IEnumerable<QuestionResult> results)
    {
        var list = results.ToList();
        decimal totalWeight = list.Sum(r => r.Weight);
        decimal weightedSum = list.Sum(r => (decimal)r.Answer * r.Weight);

        return totalWeight == 0
            ? 0
            : Math.Round(weightedSum / totalWeight, 2, MidpointRounding.AwayFromZero);
    }


    // public static Dictionary<string, decimal> CalculateWeightedAverages(Dictionary<string, AssessmentResult[]> results) =>
    //     results.ToDictionary(kvp => kvp.Key, kvp => WeightedAverage(kvp.Value));

    /// <summary>
    /// Computes the page-weighted total score for one source's pages.
    /// Returns 0 if any page has all-zero answers (not yet filled in).
    /// </summary>
    private static decimal CalculateSourceScore(IEnumerable<PageResult> pages)
    {
        var pageList = pages.ToList();
        if (pageList.Any(p => p.QuestionResults.All(q => q.Answer == 0))) return 0;

        decimal totalWeight = pageList.Sum(p => p.Weight);
        if (totalWeight == 0) return 0;

        decimal weightedSum = pageList.Sum(p => p.WeightedAverage * p.Weight);
        return Math.Round(weightedSum / totalWeight, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Calculates the total weighted average across multiple sources
    /// For each page, averages the weighed average across all sources that filled it
    /// Returns 0 if any page was not filled by at least one source.
    /// </summary>
    public static decimal CalculateTotalWeightedAverage(IEnumerable<SourceResult> sourceResults)
    {
        // Materialize once so the collection can be safely iterated multiple times
        var sourceList = sourceResults.ToList();
        var allPageNames = sourceList.SelectMany(s => s.PageResults.Select(p => p.PageName)).Distinct();

        // For each page, compute the average weighted average across all forms that filled it.
        // A page is considered filled if at least one of its answers is non-zero.
        // If no form filled a page, the aggregate average is null — used later to return 0.
        var pageAggregates = allPageNames.Select(pageName =>
        {
            var filledPages = sourceList
                .Select(s => s.PageResults.FirstOrDefault(p => p.PageName == pageName))
                .Where(p => p != null && p.QuestionResults.Any(q => q.Answer != 0))
                .ToList();

            if (filledPages.Count == 0)
                return (Average: null, Weight: 0);

            var weight = filledPages[0]!.Weight;
            var averageAnswer = filledPages.Average(p => p!.WeightedAverage);

            return (Average: (decimal?)averageAnswer, Weight: weight);
        }).ToList();

        // Only return a meaningful total if every page was filled in by at least one form
        if (pageAggregates.Any(p => p.Average == null)) return 0;

        decimal totalWeight = pageAggregates.Sum(r => r.Weight);
        decimal weightedSum = pageAggregates.Sum(p => p.Average!.Value * p.Weight);
        return totalWeight == 0 ? 0 : Math.Round(weightedSum / totalWeight, 2, MidpointRounding.AwayFromZero);
    }
}