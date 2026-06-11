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
                            Name = field.Name,
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
                    Name = page.Name,
                    Weight = questions.Sum(q => q.Weight),
                    WeightedAverage = CalculatePageWeightedAverage(questions),
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

    private static decimal CalculatePageWeightedAverage(IEnumerable<QuestionResult> results)
    {
        var list = results.ToList();
        decimal totalWeight = list.Sum(r => r.Weight);
        decimal weightedSum = list.Sum(r => (decimal)r.Answer * r.Weight);

        return totalWeight == 0
            ? 0
            : weightedSum / totalWeight;
    }

    private static decimal CalculateSourceWeightedAverage(IEnumerable<PageResult> pages)
    {
        var pageList = pages.ToList();
        if (pageList.Any(p => p.QuestionResults.All(q => q.Answer == 0))) return 0;

        decimal totalWeight = pageList.Sum(p => p.Weight);
        if (totalWeight == 0) return 0;

        decimal weightedSum = pageList.Sum(p => p.WeightedAverage * p.Weight);
        return weightedSum / totalWeight;
    }
}