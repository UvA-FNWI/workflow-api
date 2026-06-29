using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Assessments;

public static class AssessmentService
{
    public static float CalculateFinalGrade(AssessmentConfiguration config,
        IEnumerable<AssessmentPartResult> partResults)
    {
        var totalPartWeight = config.Parts.Sum(p => p.Weight);
        if (totalPartWeight <= 0) return 0;

        var partsWithResults = config.Parts
            .Select(part => (Part: part, Result: partResults.FirstOrDefault(r => r.Name == part.Name)))
            .Where(pair => pair.Result != null && pair.Result.Combined.WeightedAverage != 0)
            .ToList();

        if (partsWithResults.Count == 0) return 0;

        var submittedWeight = partsWithResults.Sum(pair => pair.Part.Weight);
        var weightedSum = partsWithResults
            .Sum(pair => pair.Result!.Combined.WeightedAverage * pair.Part.Weight);

        return ApplyRounding(weightedSum / submittedWeight, config);
    }

    public static float ApplyRounding(decimal grade, AssessmentConfiguration config)
    {
        var rounded = config switch
        {
            { GradingBasis: GradingBasis.PassFail }
                => grade >= 6m ? 1f : 0f,
            { GradeGap: true } when grade is > 5m and < 6m
                => (float)Math.Round(grade, 0, MidpointRounding.AwayFromZero),
            { GradingBasis: GradingBasis.Half }
                => grade is > 5.4m and < 5.5m
                    ? 5.0f
                    : (float)Math.Round(grade * 2, 0, MidpointRounding.AwayFromZero) / 2,
            { GradingBasis: GradingBasis.Decimal }
                => (float)Math.Round(grade is > 5.4m and < 5.5m ? 5.4m : grade, 1, MidpointRounding.AwayFromZero),
            _
                => (float)Math.Round(grade, 1, MidpointRounding.AwayFromZero)
        };

        return config.GradingBasis is GradingBasis.PassFail ? rounded : Math.Clamp(rounded, 1f, 10f);
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

    public static SourceResult CalculateCombined(AssessmentPart partConfig, ICollection<SourceResult> sourceResults)
    {
        var totalWeight = partConfig.Sources.Sum(x => x.Weight);

        return new SourceResult
        {
            Name = SourceResult.Combined,
            WeightedAverage = CalculatePartWeightedAverage(partConfig, sourceResults),
            PageResults = sourceResults.FirstOrDefault()?.PageResults
                .Select(p =>
                {
                    var results = sourceResults
                        .ToDictionary(s => s.Name, s => s.PageResults.FirstOrDefault(q => q.Name == p.Name)!);
                    if (partConfig.Sources.Any(x => results.GetValueOrDefault(x.Name) == null))
                        return null;
                    return new PageResult
                    {
                        Name = p.Name,
                        WeightedAverage = results.Values.Any(v => v.WeightedAverage != null)
                            ? partConfig.Sources.Sum(x => results[x.Name].WeightedAverage * x.Weight) / totalWeight
                            : null,
                        Weight = results.Values.FirstOrDefault(v => v.Weight != null)?.Weight,
                        Sum = partConfig.Sources.Sum(x => results[x.Name].Sum * x.Weight) / totalWeight,
                        QuestionResults = p.QuestionResults.Select(q => new QuestionResult
                        {
                            Name = q.Name,
                            Answer = partConfig.Sources.Sum(x =>
                                (results[x.Name].QuestionResults.FirstOrDefault(z => z.Name == q.Name)?.Answer ?? 0) *
                                (double)x.Weight) / (double)totalWeight,
                            Weight = q.Weight,
                            Percentage = q.Percentage
                        }).ToList()
                    };
                })
                .Where(p => p != null)
                .Cast<PageResult>()
                .ToList() ?? []
        };
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
            .Where(r => r.Type is CalculationType.Sum)
            .Sum(r => (decimal)r.Answer);

    private static decimal? CalculatePageWeightedAverage(ICollection<QuestionResult> results)
    {
        var totalWeight = results.Where(r => r.Weight != null).Sum(r => r.Weight);
        var weightedSum = results.Where(r => r.Weight != null).Sum(r => (decimal)r.Answer * r.Weight);

        return totalWeight == 0 ? null : weightedSum / totalWeight;
    }

    private static decimal CalculateSourceWeightedAverage(IEnumerable<PageResult> pages)
    {
        var pageList = pages.ToList();

        decimal totalWeight = pageList.Where(p => p.Weight != null).Sum(p => p.Weight!.Value);
        if (totalWeight == 0) return 0;

        decimal weightedSum = pageList
            .Where(p => p.Weight != null && p.WeightedAverage != null)
            .Sum(p => p.WeightedAverage!.Value * p.Weight!.Value);
        return weightedSum / totalWeight + pageList.Sum(s => s.Sum);
    }
}