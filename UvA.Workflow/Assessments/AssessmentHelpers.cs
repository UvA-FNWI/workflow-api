using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Assessments;

public static class AssessmentHelpers
{
    public static (decimal Unrounded, float Rounded) CalculateFinalGrade(AssessmentConfiguration config,
        IEnumerable<AssessmentPartResult> partResults)
    {
        var totalPartWeight = config.Parts.Sum(p => p.Weight);
        if (totalPartWeight <= 0) return (0, 0);

        var partsWithResults = config.Parts
            .Select(part => (Part: part, Result: partResults.FirstOrDefault(r => r.Name == part.Name)))
            .Where(pair => pair.Result != null && pair.Result.Combined.WeightedAverage != 0)
            .ToList();

        if (partsWithResults.Count == 0) return (0, 0);

        var submittedWeight = partsWithResults.Sum(pair => pair.Part.Weight);
        var weightedSum = partsWithResults
            .Sum(pair => pair.Result!.Combined.WeightedAverage * pair.Part.Weight);

        var unrounded = weightedSum / submittedWeight;
        return (unrounded, ApplyRounding(unrounded, config));
    }

    public static float ApplyRounding(decimal grade, AssessmentConfiguration config)
    {
        var rounded = config switch
        {
            { GradingBasis: GradingBasis.PassFail }
                => grade >= 5.5m ? 1f : 0f,
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
        var sources = partConfig.Sources
            .Join(sourceResults, config => config.Name, result => result.Name,
                (config, result) => (config.Weight, result.PageResults))
            .ToArray();

        return new SourceResult
        {
            Name = SourceResult.Combined,
            WeightedAverage = CalculatePartWeightedAverage(partConfig, sourceResults),
            PageResults = sources.Length == partConfig.Sources.Count
                ? sources.SelectMany(source => source.PageResults.Select(page => (source.Weight, Page: page)))
                    .GroupBy(entry => entry.Page.Name)
                    .Select(group =>
                    {
                        var results = group.ToArray();
                        var totalWeight = results.Sum(entry => entry.Weight);
                        var firstPage = results[0].Page;
                        return new PageResult
                        {
                            Name = group.Key,
                            WeightedAverage = totalWeight != 0 &&
                                              results.Any(entry => entry.Page.WeightedAverage != null)
                                ? results.Sum(entry => entry.Page.WeightedAverage * entry.Weight) / totalWeight
                                : null,
                            Weight =
                                results.Select(entry => entry.Page.Weight).FirstOrDefault(weight => weight != null),
                            Sum = totalWeight == 0
                                ? 0
                                : results.Sum(entry => entry.Page.Sum * entry.Weight) / totalWeight,
                            QuestionResults = firstPage.QuestionResults
                                .Where(question => totalWeight != 0 && results.All(entry =>
                                    entry.Page.QuestionResults.Any(candidate => candidate.Name == question.Name)))
                                .Select(question => new QuestionResult
                                {
                                    Name = question.Name,
                                    Answer = results.Sum(entry => entry.Page.QuestionResults
                                                                      .First(candidate =>
                                                                          candidate.Name == question.Name).Answer *
                                                                  (double)entry.Weight) / (double)totalWeight,
                                    Weight = question.Weight,
                                    Percentage = question.Percentage
                                }).ToList()
                        };
                    }).ToList()
                : [],
            Form = sourceResults.FirstOrDefault()?.Form!
        };
    }

    public static Dictionary<Page, decimal?> GetPageWeights(IEnumerable<Page> pages, ObjectContext context) =>
        pages.ToDictionary(page => page, page =>
        {
            var weights = page.Questions.OfType<WeightedPropertyDefinition>()
                .Where(field => field.Condition.IsMet(context))
                .Select(field => field.Calculation?.Weight)
                .OfType<decimal>()
                .ToArray();
            return weights.Length == 0 ? (decimal?)null : weights.Sum();
        });

    public static SourceResult CalculateSourceResult(Form form, ObjectContext context,
        string? pageName)
    {
        var formContext = form.GetContext(context);
        var pages = form.GetCurrentPages().Where(page => page.Condition.IsMet(formContext)).ToArray();
        var pageWeights = GetPageWeights(pages, formContext);
        var totalWeight = pageWeights.Values.Sum() ?? 0;

        var pageResults = pages
            .Where(page =>
                page.Questions.OfType<WeightedPropertyDefinition>().Any(field =>
                    field.Calculation != null)) // Filter out pages without calculation
            .Where(page => string.IsNullOrEmpty(pageName) || page.Name == pageName)
            .Select(page =>
            {
                var questions = page.Questions
                    .OfType<WeightedPropertyDefinition>()
                    .Where(field => field.Calculation != null)
                    .Where(field => field.Condition.IsMet(formContext))
                    .Select(field =>
                    {
                        var answerKey = context.Get(
                            form.PropertyName != null ? $"{form.PropertyName}.{field.Name}" : field.Name);
                        var weight = field.Calculation?.Weight;
                        return new QuestionResult
                        {
                            Name = field.Name,
                            Weight = weight,
                            Type = field.Calculation!.Type,
                            Percentage = totalWeight == 0 || weight == null
                                ? null
                                : weight / totalWeight * 100,
                            Answer = answerKey switch
                            {
                                double d => d,
                                string s => (field as ChoicePropertyDefinition)?.Values
                                    ?.FirstOrDefault(v => v.Name == s)?.Value ?? 0,
                                _ => 0
                            }
                        };
                    }).ToList();

                return new PageResult
                {
                    Name = page.Name,
                    Weight = pageWeights[page],
                    WeightedAverage = CalculatePageWeightedAverage(questions),
                    Sum = CalculatePageSum(questions),
                    QuestionResults = questions
                };
            }).ToList();

        return new SourceResult
        {
            Name = form.Name,
            WeightedAverage = CalculateSourceWeightedAverage(pageResults),
            PageResults = pageResults,
            Form = form
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