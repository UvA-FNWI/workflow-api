namespace UvA.Workflow.Api.Submissions.Dtos;

public record FormDto(
    string Name,
    BilingualString Title,
    PageDto[] Pages,
    FormLayout Layout,
    bool HasResults)
{
    public static FormDto Create(Form form)
    {
        var filteredPages = form.ActualForm.Pages.Values
            .Where(p => p.Sources == null || p.Sources.Contains(form.Property))
            .ToArray();
        var questions = filteredPages
            .SelectMany(p => p.Fields)
            .ToDictionary(q => q, QuestionDto.Create);
        form = form.ActualForm;
        return new FormDto(
            form.Name,
            form.Title ?? form.Name,
            filteredPages.Select((p, i) => PageDto.Create(i, p, p.Fields.Select(q => questions[q]))).ToArray(),
            form.Layout,
            form.WorkflowDefinition.Results != null
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
    public static PageDto Create(int index, Page page, IEnumerable<QuestionDto> questions)
        => new(
            index,
            page.DisplayTitle,
            page.Introduction,
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
    TableSettingsDto? TableSettings,
    bool HideInResults)
{
    public static QuestionDto Create(PropertyDefinition propertyDefinition) => new(
        $"{propertyDefinition.ParentType.Name}_{propertyDefinition.Name}",
        propertyDefinition.Name,
        propertyDefinition.DisplayName,
        propertyDefinition.DataType, propertyDefinition.IsRequired, propertyDefinition.IsArray,
        propertyDefinition.Values?.Values.Select(v => new ChoiceDto(v.Name, v.Text ?? v.Name, v.Description)).ToArray(),
        propertyDefinition.WorkflowDefinition?.Name,
        propertyDefinition.Description,
        propertyDefinition.ShortDisplayName,
        propertyDefinition.Layout,
        propertyDefinition.Table == null ? null : TableSettingsDto.Create(propertyDefinition.Table),
        propertyDefinition.HideInResults
    );
}

public record TableSettingsDto(FormDto Form, TableLayout Layout)
{
    public static TableSettingsDto Create(TableSettings tableSettings) => new(
        FormDto.Create(tableSettings.Form), tableSettings.Layout
    );
}

public record ChoiceDto(string Name, BilingualString Text, BilingualString? Description);