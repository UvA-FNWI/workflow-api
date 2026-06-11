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
    decimal? WeightedAverage,
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

    private decimal RoundToTwo(decimal input) => Math.Round(input, 2, MidpointRounding.AwayFromZero);

    public AssessmentDto Create(
        string id,
        IEnumerable<SubmissionContext> contexts,
        AssessmentConfiguration? assessmentConfig,
        string? pageName = null)
    {
        var contextList = contexts.ToList();
        var parts = new List<AssessmentPartDto>();
        // We need this separate list to pass domain types into CalculateFinalGrade
        var domainPartResults = new List<AssessmentPartResult>();

        decimal totalPartWeight = assessmentConfig?.Parts.Sum(p => p.Weight) ?? 0;

        foreach (var partConfig in assessmentConfig?.Parts ?? [])
        {
            // 1. Find the contexts that belong to this part's sources
            var partContexts = partConfig.Sources
                .Select(source => contextList.FirstOrDefault(c => c.Form.Name == source.Name))
                .Where(c => c != null)
                .Cast<SubmissionContext>()
                .ToList();

            // 2. Service calculates pure domain results (numbers only, no API concerns)
            var sourceResults = partContexts
                .Select(c => AssessmentService.CalculateSourceResult(c, pageName))
                .ToList();

            // 3. Service calculates the part average from those domain results
            var partAverage = AssessmentService.CalculatePartWeightedAverage(partConfig, sourceResults);

            // 4. Store domain result so CalculateFinalGrade can use it later
            domainPartResults.Add(new AssessmentPartResult
            {
                Name = partConfig.Name,
                WeightedAverage = partAverage,
                SourceResults = sourceResults
            });

            // percentage of this part within the whole assessment
            decimal partPercentage = totalPartWeight > 0
                ? partConfig.Weight / totalPartWeight * 100
                : 0;

            // percentage of each source within this part
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
                RoundToTwo(partAverage),
                RoundToTwo(partPercentage)
            ));
        }

        // 6. Final grade uses domain results, not the DTOs
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

    // Takes a pre-computed SourceResult — the service already did the math.
    // This method only adds the API-specific data: answers and display title.
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

        var roundedPageResults = sourceResult.PageResults
            .Select(pr => new PageResult
            {
                Name = pr.Name,
                Weight = pr.Weight,
                WeightedAverage = RoundToTwo(pr.WeightedAverage),
                QuestionResults = pr.QuestionResults
                    .Select(qr => new QuestionResult
                    {
                        Name = qr.Name,
                        Weight = qr.Weight,
                        Percentage = RoundToTwo(qr.Percentage),
                        Answer = qr.Answer
                    })
                    .ToList()
            })
            .ToArray();

        return new(
            context.Form.Name,
            context.Form.DisplayName,
            roundedPageResults,
            answers,
            RoundToTwo(sourceResult.WeightedAverage),
            percentage != null ? RoundToTwo(percentage.Value) : null
        );
    }
}