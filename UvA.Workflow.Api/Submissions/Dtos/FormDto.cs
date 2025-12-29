namespace UvA.Workflow.Api.Submissions.Dtos;

public record FormDto(
    string Name,
    BilingualString Title,
    PageDto[] Pages,
    FormLayout Layout,
    bool HasResults,
    string? Step)
{
    public static FormDto Create(Form form, ObjectContext context)
    {
        var filteredPages = form.ActualForm.Pages
            .Where(p => p.Sources == null || p.Sources.Contains(form.PropertyName))
            .ToArray();
        var questions = filteredPages
            .SelectMany(p => p.Fields)
            .ToDictionary(q => q, q => QuestionDto.Create(q, context));
        form = form.ActualForm;
        return new FormDto(
            form.Name,
            form.Title ?? form.Name,
            filteredPages.Select((p, i) => PageDto.Create(i, p, p.Fields.Select(q => questions[q]), context)).ToArray(),
            form.Layout,
            form.WorkflowDefinition.Results != null,
            form.Step
        );
    }
}

public record PageDto(
    int Index,
    BilingualString Title,
    BilingualString? Introduction,
    PageLayout Layout,
    QuestionDto[] Questions
)
{
    public static PageDto Create(int index, Page page, IEnumerable<QuestionDto> questions, ObjectContext context)
        => new(
            index,
            page.DisplayTitle,
            page.IntroductionTemplate?.Apply(context),
            page.Layout,
            questions.ToArray()
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
    bool HideInResults)
{
    public static QuestionDto Create(PropertyDefinition propertyDefinition, ObjectContext context) => new(
        $"{propertyDefinition.ParentType.Name}_{propertyDefinition.Name}",
        propertyDefinition.Name,
        propertyDefinition.DisplayName,
        propertyDefinition.DataType, propertyDefinition.IsRequired, propertyDefinition.IsArray,
        propertyDefinition.Values?.Values.Select(v => new ChoiceDto(v.Name, v.Text ?? v.Name, v.Description)).ToArray(),
        propertyDefinition.WorkflowDefinition?.Name,
        propertyDefinition.Description,
        propertyDefinition.ShortDisplayName,
        propertyDefinition.Layout,
        propertyDefinition is { DataType: DataType.Object, WorkflowDefinition: not null }
            ? propertyDefinition.WorkflowDefinition.Properties.Select(c => Create(c, context)).ToArray()
            : null,
        propertyDefinition.HideInResults
    );
}

public record ChoiceDto(string Name, BilingualString Text, BilingualString? Description);