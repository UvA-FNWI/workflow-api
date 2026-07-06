using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Assessments;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments.Dtos;

public record FinalGrade(
    decimal? Calculated,
    float? Rounded,
    BilingualString? Text
);

public record AssessmentDto(
    string Id,
    AssessmentPartDto[] Parts,
    FinalGrade? FinalGrade
);

public record AssessmentPartDto(
    string Id,
    BilingualString Title,
    SourceResultDto[] SourceResults,
    SourceResultDto? Combined,
    decimal? Percentage,
    bool ShowDiscrepancyWarning,
    FormDto? Form
);

public record SourceResultDto(
    string Id,
    BilingualString Title,
    PageResult[] PageResults,
    AnswerDto[] Answers,
    decimal? WeightedAverage,
    decimal? Percentage
);

public class AssessmentDtoFactory(ArtifactTokenService artifactTokenService, ModelService modelService)
{
    private readonly AnswerDtoFactory _answerDtoFactory = new(artifactTokenService);

    [return: NotNullIfNotNull(nameof(input))]
    private decimal? RoundToTwo(decimal? input) =>
        input == null ? null : Math.Round(input.Value, 2, MidpointRounding.AwayFromZero);

    public AssessmentDto Create(
        string id,
        IEnumerable<SubmissionContext> contexts,
        AssessmentConfiguration? assessmentConfig,
        string? pageName = null,
        string[]? allowedForms = null)
    {
        var contextList = contexts.ToList();
        var parts = new List<AssessmentPartDto>();
        var domainPartResults = new List<AssessmentPartResult>();

        decimal totalPartWeight = assessmentConfig?.Parts.Sum(p => p.Weight) ?? 0;

        foreach (var partConfig in assessmentConfig?.Parts ?? [])
        {
            var partContexts = partConfig.Sources
                .Select(source => contextList.FirstOrDefault(c => c.Form.Name == source.Name))
                .Where(c => c != null)
                .Cast<SubmissionContext>()
                .ToList();

            var sourceResults = partContexts
                .Select(c => AssessmentService.CalculateSourceResult(c, pageName))
                .ToList();

            var result = new AssessmentPartResult
            {
                Name = partConfig.Name,
                Combined = AssessmentService.CalculateCombined(partConfig, sourceResults),
                SourceResults = sourceResults
            };
            domainPartResults.Add(result);

            decimal partPercentage = totalPartWeight > 0
                ? partConfig.Weight / totalPartWeight * 100
                : 0;

            decimal totalSourceWeight = partConfig.Sources.Sum(s => s.Weight);
            var sourceResultDtos = partContexts
                .Where(c => allowedForms == null || allowedForms.Contains(c.Form.Name))
                .Select((context, i) =>
                {
                    var sourceConfig = partConfig.Sources.FirstOrDefault(s => s.Name == context.Form.Name);
                    decimal sourcePercentage = totalSourceWeight > 0 && sourceConfig != null
                        ? sourceConfig.Weight / totalSourceWeight * 100
                        : 0;
                    return MapToSourceResultDto(context, sourceResults[i], pageName, sourcePercentage,
                        sourceConfig?.Title);
                })
                .ToArray();

            parts.Add(new AssessmentPartDto(
                partConfig.Name,
                partConfig.Title ?? partConfig.Name, // BilingualString: use configured title or fall back to name
                sourceResultDtos,
                partContexts.Count > 0 ? MapToSourceResultDto(partContexts[0], result.Combined, null) : null,
                RoundToTwo(partPercentage),
                partConfig.MaximumDiscrepancy > 0 && sourceResultDtos.Any(s1 => s1.WeightedAverage != null
                    && sourceResultDtos.Any(s2 =>
                        s2.WeightedAverage != null && Math.Abs(s2.WeightedAverage.Value - s1.WeightedAverage.Value) >=
                        partConfig.MaximumDiscrepancy)),
                partContexts.Count > 0
                    ? FormDto.Create(partContexts[0].Form, ObjectContext.Create(partContexts[0].Instance, modelService))
                    : null
            ));
        }

        if (assessmentConfig == null) return new AssessmentDto(id, parts.ToArray(), null);

        bool isGradingComplete = assessmentConfig.Parts
            .All(p => p.Sources.All(s => contextList.Any(c => c.Form.Name == s.Name)));

        if (!isGradingComplete)
            return new AssessmentDto(id, parts.ToArray(), null);

        var (calculatedFinalGradeUnrounded, calculatedFinalGradeRounded) =
            AssessmentService.CalculateFinalGrade(assessmentConfig, domainPartResults);

        var finalGradeLabel = assessmentConfig.GradingBasis == GradingBasis.PassFail
            ? calculatedFinalGradeRounded >= 1
                ? new BilingualString("Pass", "Voldoende")
                : new BilingualString("Fail", "Onvoldoende")
            : null;

        return new AssessmentDto(id, parts.ToArray(), new FinalGrade(calculatedFinalGradeUnrounded,
            finalGradeLabel == null ? calculatedFinalGradeRounded : null,
            finalGradeLabel));
    }

    public SourceResultDto CreateSourceResults(SubmissionContext context, string? pageName = null)
    {
        var sourceResult = AssessmentService.CalculateSourceResult(context, pageName);
        return MapToSourceResultDto(context, sourceResult, pageName);
    }

    private SourceResultDto MapToSourceResultDto(
        SubmissionContext context,
        SourceResult sourceResult,
        string? pageName,
        decimal? percentage = null,
        BilingualString? titleOverride = null)
    {
        var shownQuestionIds = modelService.GetQuestionStatus(context.Instance, context.Form, true);
        var questionNamesOnPage = context.Form.ActualForm.Pages
            .Where(p => string.IsNullOrEmpty(pageName) || p.Name == pageName)
            .SelectMany(p => p.Fields)
            .Select(f => f.Name);

        var answers = Answer.Create(context.Instance, context.Form, shownQuestionIds)
            .Where(a => questionNamesOnPage.Contains(a.QuestionName))
            .Select(a => _answerDtoFactory.Create(a))
            .ToArray();

        if (sourceResult.IsCombined)
        {
            var definition = context.Form.ActualForm.WorkflowDefinition;
            var relevantQuestions = definition.Properties.Where(p => p.Results != null).ToDictionary(p => p.Name);
            var combinedAnswers = sourceResult.PageResults.SelectMany(p => p.QuestionResults).ToDictionary(q => q.Name);
            answers = answers
                .Where(a => relevantQuestions.ContainsKey(a.QuestionName) ||
                            combinedAnswers.ContainsKey(a.QuestionName))
                .Select(a =>
                {
                    var question = relevantQuestions.GetValueOrDefault(a.QuestionName);
                    var settings = question?.Results;
                    return a with
                    {
                        Value = settings?.Type switch
                        {
                            ResultType.Source when settings.Source != null =>
                                Answer.GetValue(question!,
                                    context.Instance.GetProperty(settings.Source, a.QuestionName)),
                            _ when !combinedAnswers.ContainsKey(a.QuestionName) => null,
                            null or ResultType.Average =>
                                JsonSerializer.SerializeToElement(Math.Round(combinedAnswers[a.QuestionName].Answer,
                                    2)),
                            _ => throw new InvalidOperationException(
                                $"Incorrect result configuration for ${a.QuestionName}")
                        }
                    };
                })
                .ToArray();
        }

        var roundedPageResults = sourceResult.PageResults
            .Select(pr => new PageResult
            {
                Name = pr.Name,
                Weight = pr.Weight,
                WeightedAverage = RoundToTwo(pr.WeightedAverage),
                Sum = RoundToTwo(pr.Sum).Value,
                QuestionResults = pr.QuestionResults
                    .Select(qr => new QuestionResult
                    {
                        Name = qr.Name,
                        Weight = qr.Weight,
                        Percentage = RoundToTwo(qr.Percentage),
                        Answer = Math.Round(qr.Answer, 2)
                    })
                    .ToList()
            })
            .ToArray();

        return new(
            sourceResult.IsCombined ? "Combined" : context.Form.Name,
            sourceResult.IsCombined ? new("Average", "Gemiddelde") : (titleOverride ?? context.Form.DisplayName),
            roundedPageResults,
            answers,
            RoundToTwo(sourceResult.WeightedAverage),
            percentage != null ? RoundToTwo(percentage.Value) : null
        );
    }
}