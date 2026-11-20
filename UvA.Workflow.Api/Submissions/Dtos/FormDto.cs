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
    string? PropertyName)
{
    public static FormDto Create(Form form, ObjectContext context)
    {
        context = form.GetContext(context);
        var allPages = form.ActualForm.Pages.ToArray();
        var currentFormPages = form.GetCurrentPages().ToArray();
        var activePages = currentFormPages.Where(p => p.Condition.IsMet(context)).ToArray();
        var pageWeights =
            AssessmentHelpers.GetPageWeights(allPages.Where(page => page.Condition.IsMet(context)), context);
        var totalWeight = pageWeights.Values.Sum() ?? 0;

        var questions = activePages
            .SelectMany(p => p.Questions)
            .Distinct()
            .ToDictionary(q => q, q => QuestionDto.Create(
                q,
                totalWeight,
                form.WorkflowDefinition,
                form.PropertyName == null ? null : $"{form.PropertyName}.{q.Name}"));
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
                    ? p.PageElements.Select(e => PageElementDto.Create(e, questions, context))
                        .OfType<PageElementDto>()
                    : [];
                decimal? percentage = isActive && totalWeight != 0 && pageWeights.GetValueOrDefault(p) is { } weight
                    ? Math.Round(weight / totalWeight * 100, 2, MidpointRounding.AwayFromZero)
                    : null;
                return PageDto.Create(i, p, pageElements, isInCurrentForm, isActive, percentage);
            }).ToArray(),
            form.Layout,
            originalForm.Step,
            originalForm.PropertyName
        );
    }
}