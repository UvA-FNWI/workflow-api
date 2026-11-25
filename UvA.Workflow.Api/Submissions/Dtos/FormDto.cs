using UvA.Workflow.Assessments;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record FormDto(
    string Name,
    BilingualString Title,
    PageDto[] Pages,
    FormLayout Layout,
    string? Step,
    FormConfirmationDto? Confirmation = null)
{
    public static FormDto Create(Form form, ObjectContext context, bool preview = false)
    {
        context = form.GetContext(context);
        var allPages = form.ActualForm.Pages.ToArray();
        var currentFormPages = form.GetCurrentPages().ToArray();
        var activePages =
            preview ? currentFormPages : currentFormPages.Where(p => p.Condition.IsMet(context)).ToArray();
        var weightedPages = preview ? allPages : allPages.Where(page => page.Condition.IsMet(context));
        var pageWeights =
            AssessmentHelpers.GetPageWeights(weightedPages, context, includeConditionalQuestions: preview);
        var totalWeight = pageWeights.Values.Sum() ?? 0;

        var questions = activePages
            .SelectMany(p => p.Questions)
            .Distinct()
            .ToDictionary(q => q, q => QuestionDto.Create(q, totalWeight));
        // Prefer the overriding form's own title; fall back to the target form's title, then its name.
        var title = form.Title ?? form.ActualForm.Title ?? form.ActualForm.Name;
        var originalForm = form;
        form = form.ActualForm;
        return new FormDto(
            form.Name,
            title,
            allPages.Select((p, i) =>
            {
                var isInCurrentForm = currentFormPages.Any(page => page.Name == p.Name);
                var isActive = activePages.Any(page => page.Name == p.Name);
                var pageElements = isActive
                    ? p.PageElements.Select(e => PageElementDto.Create(e, questions, context, preview))
                        .OfType<PageElementDto>()
                    : [];
                decimal? percentage = isActive && totalWeight != 0 && pageWeights.GetValueOrDefault(p) is { } weight
                    ? Math.Round(weight / totalWeight * 100, 2, MidpointRounding.AwayFromZero)
                    : null;
                return PageDto.Create(i, p, pageElements, isInCurrentForm, isActive, percentage);
            }).ToArray(),
            form.Layout,
            originalForm.Step,
            preview ? null : FormConfirmationDto.Create(form.Confirmation, context)
        );
    }
}

public record FormConfirmationDto(
    BilingualString Title,
    BilingualString Text)
{
    public static FormConfirmationDto? Create(FormConfirmation? confirmation, ObjectContext context)
        => confirmation == null
            ? null
            : new FormConfirmationDto(
                confirmation.TitleTemplate.Apply(context),
                confirmation.TextTemplate.Apply(context));
}