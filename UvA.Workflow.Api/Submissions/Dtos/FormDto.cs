namespace UvA.Workflow.Api.Submissions.Dtos;

public record FormDto(
    string Name,
    BilingualString Title,
    PageDto[] Pages,
    FormLayout Layout,
    FormType FormType,
    string? Step)
{
    public static FormDto Create(Form form, ObjectContext context)
    {
        var allPages = form.ActualForm.Pages.ToArray();

        // For child forms, only pages matching Sources belong to the current form. For base forms, all pages are considered part of the current form.
        var currentFormPages = form.TargetForm == null
            ? allPages
            : allPages
                .Where(p => p.Sources == null ||
                            (form.PropertyName != null && p.Sources.Contains(form.PropertyName)))
                .ToArray();

        var questions = currentFormPages
            .SelectMany(p => p.Fields)
            .ToDictionary(q => q, q => QuestionDto.Create(q, context));
        var formType = form.FormType;
        form = form.ActualForm;
        return new FormDto(
            form.Name,
            form.Title ?? form.Name,
            allPages.Select((p, i) =>
            {
                var isInCurrentForm = currentFormPages.Any(page => page.Name == p.Name);
                var pageQuestions = isInCurrentForm
                    ? p.Fields.Select(q => questions[q])
                    : Enumerable.Empty<QuestionDto>();
                return PageDto.Create(i, p, pageQuestions, context, isInCurrentForm);
            }).ToArray(),
            form.Layout,
            formType,
            form.Step
        );
    }
}

public record PageDto(
    int Index,
    string Name,
    BilingualString Title,
    BilingualString? Introduction,
    PageLayout Layout,
    QuestionDto[] Questions,
    bool HasResults,
    bool IsInCurrentForm
)
{
    public static PageDto Create(int index, Page page, IEnumerable<QuestionDto> questions, ObjectContext context,
        bool isInCurrentForm)
        => new(
            index,
            page.Name,
            page.DisplayTitle,
            page.IntroductionTemplate?.Apply(context),
            page.Layout,
            questions.ToArray(),
            page.HasResults,
            isInCurrentForm
        );
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
    int? Weight,
    int? MaxLength)
{
    public static QuestionDto Create(PropertyDefinition propertyDefinition, ObjectContext context) => new(
        $"{propertyDefinition.ParentType.Name}_{propertyDefinition.Name}",
        propertyDefinition.Name,
        propertyDefinition.DisplayName,
        propertyDefinition.DataType, propertyDefinition.IsRequired, propertyDefinition.IsArray,
        propertyDefinition.Values?.Select(v => new ChoiceDto(v.Name, v.Text ?? v.Name, v.Description)).ToArray(),
        propertyDefinition.WorkflowDefinition?.Name,
        propertyDefinition.Description,
        propertyDefinition.ShortDisplayName,
        propertyDefinition.Layout,
        propertyDefinition is { DataType: DataType.Object, WorkflowDefinition: not null }
            ? propertyDefinition.WorkflowDefinition.Properties.Select(c => Create(c, context)).ToArray()
            : null,
        propertyDefinition.HideInResults,
        propertyDefinition.Weight,
        propertyDefinition.Validation?.Value?.MaxLength
    );
}

public record ChoiceDto(string Name, BilingualString Text, BilingualString? Description);