using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public record FormDto(
    string Name,
    BilingualString Title,
    PageDto[] Pages,
    FormLayout Layout,
    string? Step)
{
    public static FormDto Create(Form form, ObjectContext context)
    {
        var allPages = form.ActualForm.Pages.ToArray();
        var totalWeight = allPages
            .SelectMany(p => p.Questions)
            .OfType<WeightedPropertyDefinition>()
            .Sum(q => q.Calculation?.Weight ?? 0);

        // For child forms, only pages matching Sources belong to the current form. For base forms, all pages are considered part of the current form.
        var currentFormPages = form.TargetForm == null
            ? allPages
            : allPages
                .Where(p => p.Sources == null ||
                            (form.PropertyName != null && p.Sources.Contains(form.PropertyName)))
                .ToArray();

        var activePages = currentFormPages.Where(p => p.Condition.IsMet(context)).ToArray();

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
                    ? p.PageElements.Select(e => PageElementDto.Create(e, questions, context))
                        .OfType<PageElementDto>()
                    : [];
                return PageDto.Create(i, p, pageElements, context, isInCurrentForm, isActive);
            }).ToArray(),
            form.Layout,
            originalForm.Step
        );
    }
}