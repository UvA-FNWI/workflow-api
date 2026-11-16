using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Assessments;
using UvA.Workflow.Submissions;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

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
    AssessmentWarningDto[] Warnings,
    FormDto? Form
);

public record AssessmentWarningDto(
    BilingualString Label
);

public record SourceResultDto(
    string Id,
    BilingualString Title,
    PageResult[] PageResults,
    AnswerDto[] Answers,
    decimal? WeightedAverage,
    decimal? Percentage
);

public class AssessmentDtoFactory(
    ArtifactTokenService artifactTokenService,
    ModelService modelService,
    IAssessmentService assessmentService
)
{
    private readonly AnswerDtoFactory _answerDtoFactory = new(artifactTokenService);

    [return: NotNullIfNotNull(nameof(input))]
    private decimal? RoundToTwo(decimal? input) =>
        input == null ? null : Math.Round(input.Value, 2, MidpointRounding.AwayFromZero);

    public AssessmentDto Create(
        WorkflowInstance instance,
        IEnumerable<SubmissionContext> contexts,
        AssessmentConfiguration? assessmentConfig,
        IReadOnlyDictionary<string, DomainAction[]> allowedHiddenByInstance,
        string? pageName = null,
        IReadOnlySet<(string InstanceId, string Form)>? allowedForms = null,
        ObjectContext? resolvedContext = null)
    {
        var contextList = contexts.ToList();
        var parts = new List<AssessmentPartDto>();
        var context = resolvedContext ?? modelService.CreateContext(instance);

        var result = assessmentService.GetAssessmentResult(
            modelService.WorkflowDefinitions[instance.WorkflowDefinition],
            context,
            assessmentConfig,
            contextList.Where(c => c.Instance.Id == instance.Id).Select(c => c.Form.Name).ToList(),
            pageName
        );

        foreach (var part in result.PartResults)
        {
            var partConfig = part.PartConfig;
            var owner = part.Owner?.Instance ?? instance;
            var allowedHidden = allowedHiddenByInstance.GetValueOrDefault(owner.Id) ?? [];
            decimal totalSourceWeight = partConfig.Sources.Sum(s => s.Weight);
            var sourceResultDtos = part.SourceResults
                .Where(c => allowedForms == null || allowedForms.Contains((owner.Id, c.Name)))
                .Select(sourceResult =>
                {
                    var sourceConfig = partConfig.Sources.FirstOrDefault(s => s.Name == sourceResult.Name);
                    decimal sourcePercentage = totalSourceWeight > 0 && sourceConfig != null
                        ? sourceConfig.Weight / totalSourceWeight * 100
                        : 0;
                    return MapToSourceResultDto(owner, sourceResult, pageName, allowedHidden, sourcePercentage,
                        sourceConfig?.Title);
                })
                .ToArray();

            // Right now we are not enriching the FormDto with context values.
            // If enriched text is necessary in the future, we should add it.
            parts.Add(new AssessmentPartDto(
                partConfig.Name,
                partConfig.Title ?? partConfig.Name, // BilingualString: use configured title or fall back to name
                sourceResultDtos,
                part.SourceResults.Count > 0
                    ? MapToSourceResultDto(owner, part.Combined, null, allowedHidden,
                        sourceForms: part.SourceResults.Select(s => s.Form))
                    : null,
                RoundToTwo(part.PartPercentage),
                partConfig.Warnings.Select(w => ToDto(w, sourceResultDtos)).OfType<AssessmentWarningDto>().ToArray(),
                part.SourceResults.Count > 0
                    ? FormDto.Create(part.SourceResults[0].Form,
                        part.Owner?.Context ?? context)
                    : null
            ));
        }

        if (assessmentConfig == null) return new AssessmentDto(instance.Id, parts.ToArray(), null);

        bool isGradingComplete = result.FinalGradeRounded != null && result.PartResults
            .All(p => p.PartConfig.Sources.All(s => contextList.Any(c =>
                c.Instance.Id == (p.Owner?.Instance.Id ?? instance.Id) && c.Form.Name == s.Name)));

        if (!isGradingComplete)
            return new AssessmentDto(instance.Id, parts.ToArray(), null);

        var finalGradeLabel = assessmentConfig.GradingBasis == GradingBasis.PassFail
            ? result.FinalGradeRounded >= 1
                ? new BilingualString("Pass", "Voldoende")
                : new BilingualString("Fail", "Onvoldoende")
            : null;

        return new AssessmentDto(instance.Id, parts.ToArray(), new FinalGrade(result.FinalGradeUnrounded,
            finalGradeLabel == null ? result.FinalGradeRounded : null,
            finalGradeLabel));
    }

    private static AssessmentWarningDto? ToDto(AssessmentWarning warning, SourceResultDto[] results)
    {
        var show = warning.MaximumDiscrepancy > 0 && results.Any(s1 => s1.WeightedAverage != null
                                                                       && results.Any(s2 =>
                                                                           s2.WeightedAverage != null &&
                                                                           Math.Abs(s2.WeightedAverage.Value -
                                                                               s1.WeightedAverage.Value) >=
                                                                           warning.MaximumDiscrepancy));
        show = show || (warning.InflectionPoint != 0
                        && results.Any(s1 => s1.WeightedAverage < warning.InflectionPoint)
                        && results.Any(s1 => s1.WeightedAverage > warning.InflectionPoint));
        return show ? new AssessmentWarningDto(warning.Label) : null;
    }

    public SourceResultDto CreateSourceResults(SubmissionContext context, IEnumerable<DomainAction> allowedHidden,
        string? pageName = null)
    {
        var sourceResult = AssessmentHelpers.CalculateSourceResult(context.Form,
            modelService.CreateContext(context.Instance),
            pageName);
        return MapToSourceResultDto(context.Instance, sourceResult, pageName, allowedHidden);
    }

    private SourceResultDto MapToSourceResultDto(
        WorkflowInstance instance,
        SourceResult sourceResult,
        string? pageName,
        IEnumerable<DomainAction> allowedHidden,
        decimal? percentage = null,
        BilingualString? titleOverride = null,
        IEnumerable<Form>? sourceForms = null)
    {
        var form = sourceResult.Form;

        var shownQuestionIds = modelService.GetQuestionStatus(instance, form, allowedHidden);
        var sourcedAnswers = new Dictionary<string, Answer>();
        if (sourceResult.IsCombined)
        {
            var sourceStatuses = sourceForms!.Select(f =>
                modelService.GetQuestionStatus(instance, f, allowedHidden)).ToArray();
            foreach (var question in form.ActualForm.PropertyDefinitions
                         .Where(q => q.Visibility == PropertyVisibility.Hidden))
            {
                var visible = sourceStatuses.All(s => s.GetValueOrDefault(question.Name)?.IsVisible == true);
                shownQuestionIds[question.Name] = shownQuestionIds[question.Name] with { IsVisible = visible };
            }

            foreach (var question in form.ActualForm.PropertyDefinitions
                         .Where(q => q.Results is { Type: ResultType.Source, Source: not null }))
            {
                var source = question.Results!.Source!;
                var statuses = modelService.WorkflowDefinitions[instance.WorkflowDefinition].Forms
                    .Where(f => f.PropertyName == source)
                    .Select(f => modelService.GetQuestionStatus(instance, f, allowedHidden)
                        .GetValueOrDefault(question.Name))
                    .OfType<QuestionStatus>().ToArray();
                var status = statuses.FirstOrDefault(s => s.IsVisible) ?? statuses.FirstOrDefault()
                    ?? shownQuestionIds[question.Name] with
                    {
                        IsVisible = question.Visibility != PropertyVisibility.Hidden &&
                                    shownQuestionIds[question.Name].IsVisible
                    };
                sourcedAnswers[question.Name] = Answer.Create(instance, form,
                    new() { [question.Name] = status }, sourceProperty: source).Single();
            }
        }

        var questionNamesOnPage = form.ActualForm.Pages
            .Where(p => string.IsNullOrEmpty(pageName) || p.Name == pageName)
            .SelectMany(p => p.Questions)
            .Select(q => q.Name);

        var answers = Answer.Create(instance, form, shownQuestionIds)
            .Where(a => questionNamesOnPage.Contains(a.QuestionName))
            .ToArray();

        if (sourceResult.IsCombined)
        {
            var definition = form.ActualForm.WorkflowDefinition;
            var relevantQuestions = definition.Properties.Where(p => p.Results != null).ToDictionary(p => p.Name);
            var combinedAnswers = sourceResult.PageResults.SelectMany(p => p.QuestionResults).ToDictionary(q => q.Name);
            answers = answers
                .Where(a => relevantQuestions.ContainsKey(a.QuestionName) ||
                            combinedAnswers.ContainsKey(a.QuestionName))
                .Select(a =>
                {
                    if (sourcedAnswers.TryGetValue(a.QuestionName, out var sourced)) return sourced;
                    if (!a.IsVisible) return a;
                    var question = relevantQuestions.GetValueOrDefault(a.QuestionName);
                    var settings = question?.Results;
                    return a with
                    {
                        Value = settings?.Type switch
                        {
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
                    .Where(qr => shownQuestionIds.GetValueOrDefault(qr.Name)?.IsVisible == true)
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
            sourceResult.IsCombined ? "Combined" : form.Name,
            sourceResult.IsCombined ? new("Average", "Gemiddelde") : (titleOverride ?? form.DisplayName),
            roundedPageResults,
            answers.Select(a => _answerDtoFactory.Create(a)).ToArray(),
            RoundToTwo(sourceResult.WeightedAverage),
            percentage != null ? RoundToTwo(percentage.Value) : null
        );
    }
}