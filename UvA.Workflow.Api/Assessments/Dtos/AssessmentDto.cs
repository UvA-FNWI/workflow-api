using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Assessments;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments.Dtos;

public record AssessmentDto(
    string Id,
    AssessmentPartDto[] Parts,
    decimal? FinalGrade
);

public record AssessmentPartDto(
    string Id,
    BilingualString Title,
    SourceResultDto[] SourceResults,
    SourceResultDto Combined,
    decimal? Percentage
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
        string? pageName = null)
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
                .Select((context, i) =>
                {
                    var sourceConfig = partConfig.Sources.FirstOrDefault(s => s.Name == context.Form.Name);
                    decimal sourcePercentage = totalSourceWeight > 0 && sourceConfig != null
                        ? sourceConfig.Weight / totalSourceWeight * 100
                        : 0;
                    return MapToSourceResultDto(context, sourceResults[i], pageName, sourcePercentage);
                })
                .ToArray();

            parts.Add(new AssessmentPartDto(
                partConfig.Name,
                partConfig.Title ?? partConfig.Name, // BilingualString: use configured title or fall back to name
                sourceResultDtos,
                MapToSourceResultDto(partContexts[0], result.Combined, null),
                RoundToTwo(partPercentage)
            ));
        }

        var finalGrade = assessmentConfig != null
            ? AssessmentService.CalculateFinalGrade(assessmentConfig, domainPartResults)
            : (decimal?)null;

        return new(id, parts.ToArray(), RoundToTwo(finalGrade ?? 0));
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
        decimal? percentage = null)
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
            var relevantQuestions =
                sourceResult.PageResults.SelectMany(p => p.QuestionResults).ToDictionary(q => q.Name);
            answers = answers
                .Where(a => relevantQuestions.ContainsKey(a.QuestionName))
                .Select(a => a with
                {
                    Value = JsonSerializer.SerializeToElement(Math.Round(relevantQuestions[a.QuestionName].Answer, 2))
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
            sourceResult.IsCombined ? new("Average", "Gemiddelde") : context.Form.DisplayName,
            roundedPageResults,
            answers,
            RoundToTwo(sourceResult.WeightedAverage),
            percentage != null ? RoundToTwo(percentage.Value) : null
        );
    }
}