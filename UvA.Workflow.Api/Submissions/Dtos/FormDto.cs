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
            .SelectMany(p => p.Fields)
            .Where(q => q.Calculation?.Weight != null)
            .Sum(q => q.Calculation!.Weight!.Value);

        // For child forms, only pages matching Sources belong to the current form. For base forms, all pages are considered part of the current form.
        var currentFormPages = form.TargetForm == null
            ? allPages
            : allPages
                .Where(p => p.Sources == null ||
                            (form.PropertyName != null && p.Sources.Contains(form.PropertyName)))
                .ToArray();

        var questions = currentFormPages
            .SelectMany(p => p.Fields)
            .Distinct()
            .ToDictionary(q => q, q => QuestionDto.Create(q, context, totalWeight));
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
                var pageElements = isInCurrentForm
                    ? p.PageElements.Select(e => PageElementDto.Create(e, questions, context))
                        .OfType<PageElementDto>()
                    : [];
                return PageDto.Create(i, p, pageElements, context, isInCurrentForm);
            }).ToArray(),
            form.Layout,
            originalForm.Step
        );
    }
}

public record PageDto(
    int Index,
    string Name,
    BilingualString Title,
    BilingualString? Introduction,
    PageLayout Layout,
    PageElementDto[] Elements,
    bool HasResults,
    bool IsInCurrentForm
)
{
    public static PageDto Create(int index, Page page, IEnumerable<PageElementDto> elements, ObjectContext context,
        bool isInCurrentForm)
        => new(
            index,
            page.Name,
            page.DisplayTitle,
            page.IntroductionTemplate?.Apply(context),
            page.Layout,
            elements.ToArray(),
            page.HasResults,
            isInCurrentForm
        );
}

public enum PageElementKind
{
    Question,
    Callout,
    Text
}

public record PageElementDto(
    PageElementKind Kind,
    QuestionDto? Question,
    CalloutDto? Callout,
    BilingualString? Text)
{
    public static PageElementDto? Create(
        PageElement element,
        Dictionary<PropertyDefinition, QuestionDto> questions,
        ObjectContext context)
    {
        if (element.Question != null)
        {
            var definition = element.QuestionDefinition
                             ?? throw new InvalidOperationException(
                                 $"PageElement question '{element.Question}' was not resolved by ModelParser");
            return new PageElementDto(PageElementKind.Question, questions[definition], null, null);
        }

        if (element.Callout != null)
            return element.Callout.Condition.IsMet(context)
                ? new PageElementDto(PageElementKind.Callout, null, CalloutDto.Create(element.Callout, context), null)
                : null;

        return new PageElementDto(PageElementKind.Text, null, null, element.TextTemplate?.Apply(context));
    }
}

public record CalloutDto(CalloutVariant Variant, BilingualString? Title, BilingualString? Text)
{
    public static CalloutDto Create(Callout callout, ObjectContext context)
        => new(callout.Variant, callout.Title, callout.TextTemplate?.Apply(context));
}

public record QuestionDto(
    string Id,
    string Name,
    BilingualString Text,
    DataType Type,
    bool IsRequired,
    bool IsArray,
    ChoiceDto[]? Choices,
    string? WorkflowDefinition,
    BilingualString? Description,
    BilingualString? ShortText,
    Dictionary<string, object>? Layout,
    QuestionDto[]? SubProperties,
    bool HideInResults,
    decimal? Weight,
    decimal? Percentage,
    int? MaxLength,
    bool? AllowsExternalUsers,
    List<RubricEntryDto>? Rubric,
    ValueSetSorting? Sorting,
    string? LinkedTo,
    IReadOnlyList<string>? AllowedFileTypes,
    int? AllowedFileSize)
{
    public static QuestionDto Create(PropertyDefinition propertyDefinition, ObjectContext context,
        decimal totalWeight)
    {
        var choices = propertyDefinition.Values?
            .Select(value => new ChoiceDto(
                value.Name,
                value.Text ?? value.Name,
                value.Description,
                value.Value))
            .ToArray();

        var subProperties = propertyDefinition is { DataType: DataType.Object, WorkflowDefinition: not null }
            ? propertyDefinition.WorkflowDefinition.Properties
                .Select(child => Create(child, context, totalWeight))
                .ToArray()
            : null;

        var weight = propertyDefinition.Calculation?.Weight;
        var percentage = totalWeight == 0 || weight == null
            ? null
            : weight / totalWeight * 100;

        var rubric = propertyDefinition.Rubric?
            .Select(entry => RubricEntryDto.Create(entry, propertyDefinition.Values))
            .ToList();

        return new QuestionDto(
            Id: $"{propertyDefinition.ParentType.Name}_{propertyDefinition.Name}",
            Name: propertyDefinition.Name,
            Text: propertyDefinition.DisplayName,
            Type: propertyDefinition.DataType,
            IsRequired: propertyDefinition.IsRequired,
            IsArray: propertyDefinition.IsArray,
            Choices: choices,
            WorkflowDefinition: propertyDefinition.WorkflowDefinition?.Name,
            Description: propertyDefinition.Description,
            ShortText: propertyDefinition.ShortDisplayName,
            Layout: propertyDefinition.Layout,
            SubProperties: subProperties,
            HideInResults: propertyDefinition.HideInResults,
            Weight: weight,
            Percentage: percentage,
            MaxLength: propertyDefinition.Validation?.Value?.MaxLength,
            AllowsExternalUsers: propertyDefinition.AllowsExternalUsers,
            Rubric: rubric,
            Sorting: propertyDefinition.Sorting,
            LinkedTo: propertyDefinition.LinkedTo,
            AllowedFileTypes: propertyDefinition.EffectiveAllowedFileTypes,
            AllowedFileSize: propertyDefinition.EffectiveAllowedFileSize
        );
    }
}

public record ChoiceDto(string Name, BilingualString Text, BilingualString? Description, double? Value);

public record RubricEntryDto(string Name, BilingualString Description, List<RubricGradeDto> Grades)
{
    public static RubricEntryDto Create(RubricEntry entry, List<Choice>? choices)
    {
        // Grades reference choice names, so the localized label comes from the matching choice's bilingual text, falling back to the name.
        var labels = choices?.ToDictionary(c => c.Name, c => c.Text ?? c.Name);
        return new RubricEntryDto(
            entry.Name,
            entry.Description,
            entry.Grades
                .Select(g => new RubricGradeDto(
                    g,
                    labels != null && labels.TryGetValue(g, out var text) ? text : g))
                .ToList()
        );
    }
}

public record RubricGradeDto(string Name, BilingualString Text);