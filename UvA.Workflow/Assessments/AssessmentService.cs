using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Assessments;

public static class AssessmentService
{
    public static decimal CalculateFinalGrade(AssessmentConfiguration config,
        IEnumerable<AssessmentPartResult> partResults)
    {
        var totalPartWeight = config.Parts.Sum(p => p.Weight);
        if (totalPartWeight <= 0) return 0;

        var partsWithResults = config.Parts
            .Select(part => (Part: part, Result: partResults.FirstOrDefault(r => r.Name == part.Name)))
            .Where(pair => pair.Result != null && pair.Result.WeightedAverage != 0)
            .ToList();

        if (partsWithResults.Count == 0) return 0;

        var submittedWeight = partsWithResults.Sum(pair => pair.Part.Weight);
        var weightedSum = partsWithResults
            .Sum(pair => pair.Result!.WeightedAverage * pair.Part.Weight);

        return weightedSum / submittedWeight;
    }

    public static decimal CalculatePartWeightedAverage(
        AssessmentPart partConfig,
        IEnumerable<SourceResult> sourceResults)
    {
        var sourceList = sourceResults.ToList();
        decimal totalSourceWeight = 0;
        decimal weightedSum = 0;

        foreach (var sourceConfig in partConfig.Sources)
        {
            var sourceResult = sourceList.FirstOrDefault(s => s.Name == sourceConfig.Name);
            if (sourceResult == null || sourceResult.WeightedAverage == 0) continue;

            weightedSum += sourceResult.WeightedAverage * sourceConfig.Weight;
            totalSourceWeight += sourceConfig.Weight;
        }

        return totalSourceWeight == 0
            ? 0
            : weightedSum / totalSourceWeight;
    }

    public static SourceResult CalculateSourceResult(SubmissionContext submissionContext,
        string? pageName)
    {
        var pages = submissionContext.Form.ActualForm.Pages.ToArray();

        var totalWeight = pages
            .SelectMany(page => page.Fields.Where(field => field.Calculation?.Weight != null))
            .Sum(field => field.Calculation!.Weight!.Value);

        var pageResults = pages
            .Where(page => page.Fields.Any(field => field.Calculation != null)) // Filter out pages without calculation
            .Where(page => string.IsNullOrEmpty(pageName) || page.Name == pageName)
            .Select(page =>
            {
                var questions = page.Fields
                    .Where(field => field.Calculation != null)
                    .Select(field =>
                    {
                        var answerKey =
                            submissionContext.Instance.GetProperty(submissionContext.Form.PropertyName, field.Name);
                        var weight = field.Calculation?.Weight;
                        return new QuestionResult
                        {
                            Name = field.Name,
                            Weight = weight,
                            Type = field.Calculation!.Type,
                            Percentage = totalWeight == 0 || weight == null
                                ? null
                                : weight / totalWeight * 100,
                            Answer = answerKey is null || answerKey.IsBsonNull
                                ? 0
                                : field.Values?.FirstOrDefault(v => v.Name == answerKey.AsString)?.Value ??
                                  answerKey.ToDouble()
                        };
                    }).ToList();

                return new PageResult
                {
                    Name = page.Name,
                    Weight = questions.Any(q => q.Weight != null)
                        ? questions.Where(q => q.Weight != null).Sum(q => q.Weight)
                        : null,
                    WeightedAverage = CalculatePageWeightedAverage(questions),
                    Sum = CalculatePageSum(questions),
                    QuestionResults = questions
                };
            }).ToList();

        return new SourceResult
        {
            Name = submissionContext.Form.Name,
            WeightedAverage = CalculateSourceWeightedAverage(pageResults),
            PageResults = pageResults
        };
    }

    private static decimal CalculatePageSum(ICollection<QuestionResult> results) =>
        results
            .Where(r => r.Type is CalculationType.Add or CalculationType.Subtract)
            .Sum(r => (decimal)Math.Abs(r.Answer) * (r.Type == CalculationType.Subtract ? -1 : 1));

    private static decimal? CalculatePageWeightedAverage(ICollection<QuestionResult> results)
    {
        var totalWeight = results.Where(r => r.Weight != null).Sum(r => r.Weight);
        var weightedSum = results.Where(r => r.Weight != null).Sum(r => (decimal)r.Answer * r.Weight);

        return totalWeight == 0 ? null : weightedSum / totalWeight;
    }

    private static decimal CalculateSourceWeightedAverage(IEnumerable<PageResult> pages)
    {
        var pageList = pages.ToList();
        if (pageList.Any(p => p.QuestionResults.All(q => q.Answer == 0))) return 0;

        decimal totalWeight = pageList.Where(p => p.Weight != null).Sum(p => p.Weight!.Value);
        if (totalWeight == 0) return 0;

        decimal weightedSum = pageList
            .Where(p => p.Weight != null && p.WeightedAverage != null)
            .Sum(p => p.WeightedAverage!.Value * p.Weight!.Value);
        return weightedSum / totalWeight + pageList.Sum(s => s.Sum);
    }
}