using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.Submissions.Dtos;

// QuestionJsonTypeInfoResolver makes sure JSON includes the fields of each question type.
// It finds new question types automatically, so they don't need JSON attributes here.
public abstract record QuestionDto
{
    protected QuestionDto(PropertyDefinition property, WorkflowDefinition workflowDefinition)
    {
        Id = $"{workflowDefinition.Name}_{property.Name}";
        Name = property.Name;
        Text = property.DisplayName;
        Type = property.DataType;
        IsRequired = property.IsRequired;
        IsArray = property.IsArray;
        Description = property.Description;
        ShortText = property.ShortDisplayName;
        HideInResults = property.HideInResults;
        LinkedTo = property.LinkedTo;
        // Inherited properties share one object across definitions; ParentType can point at a base
        // type without messages.enabled even on an instance of a derived type.
        AllowMessages = workflowDefinition.AllowsMessages(property.Name);
    }

    public string Id { get; init; }
    public string Name { get; init; }
    public BilingualString Text { get; init; }
    public DataType Type { get; }
    public bool IsRequired { get; init; }
    public bool IsArray { get; init; }
    public BilingualString? Description { get; init; }
    public BilingualString? ShortText { get; init; }
    public bool HideInResults { get; init; }
    public string? LinkedTo { get; init; }
    public bool AllowMessages { get; init; }

    public static QuestionDto Create(PropertyDefinition property, decimal totalWeight,
        WorkflowDefinition workflowDefinition)
        => property switch
        {
            StringPropertyDefinition text => new StringQuestionDto(text, workflowDefinition),
            DatePropertyDefinition date => new DateQuestionDto(date, workflowDefinition),
            DateTimePropertyDefinition dateTime => new DateTimeQuestionDto(dateTime, workflowDefinition),
            IntPropertyDefinition integer => new IntQuestionDto(integer, totalWeight, workflowDefinition),
            DoublePropertyDefinition number => new DoubleQuestionDto(number, totalWeight, workflowDefinition),
            CheckPropertyDefinition check => new CheckQuestionDto(check, workflowDefinition),
            CurrencyPropertyDefinition currency => new CurrencyQuestionDto(currency, totalWeight, workflowDefinition),
            FilePropertyDefinition file => new FileQuestionDto(file, workflowDefinition),
            UserPropertyDefinition user => new UserQuestionDto(user, workflowDefinition),
            ChoicePropertyDefinition choice => new ChoiceQuestionDto(choice, totalWeight, workflowDefinition),
            ReferencePropertyDefinition reference => new ReferenceQuestionDto(reference, workflowDefinition),
            ObjectPropertyDefinition embedded => new ObjectQuestionDto(embedded, totalWeight, workflowDefinition),
            _ => throw new ArgumentException($"Unsupported property type: {property.GetType().Name}")
        };
}

public abstract record WeightedQuestionDto : QuestionDto
{
    protected WeightedQuestionDto(WeightedPropertyDefinition property, decimal totalWeight,
        WorkflowDefinition workflowDefinition) : base(property, workflowDefinition)
    {
        Weight = property.Calculation?.Weight;
        Percentage = totalWeight == 0 || Weight == null ? null : Weight / totalWeight * 100;
    }

    public decimal? Weight { get; init; }
    public decimal? Percentage { get; init; }
}

public record StringQuestionDto : QuestionDto
{
    public StringQuestionDto(StringPropertyDefinition property, WorkflowDefinition workflowDefinition)
        : base(property, workflowDefinition)
    {
        Control = property.Control;
        MaxLength = property.Validation.GetAllValues().Select(v => v.MaxLength).FirstOrDefault(v => v != null);
        MinLength = property.Validation.GetAllValues().Select(v => v.MinLength).FirstOrDefault(v => v != null);
    }

    public StringControl? Control { get; init; }
    public int? MaxLength { get; init; }
    public int? MinLength { get; init; }
}

public record DateQuestionDto : QuestionDto
{
    public DateQuestionDto(DatePropertyDefinition property, WorkflowDefinition workflowDefinition)
        : base(property, workflowDefinition)
    {
    }
}

public record DateTimeQuestionDto : QuestionDto
{
    public DateTimeQuestionDto(DateTimePropertyDefinition property, WorkflowDefinition workflowDefinition)
        : base(property, workflowDefinition)
    {
    }
}

public record IntQuestionDto : WeightedQuestionDto
{
    public IntQuestionDto(IntPropertyDefinition property, decimal totalWeight, WorkflowDefinition workflowDefinition)
        : base(property, totalWeight, workflowDefinition)
    {
    }
}

public record DoubleQuestionDto : WeightedQuestionDto
{
    public DoubleQuestionDto(DoublePropertyDefinition property, decimal totalWeight,
        WorkflowDefinition workflowDefinition) : base(property, totalWeight, workflowDefinition)
    {
    }
}

public record CheckQuestionDto : QuestionDto
{
    public CheckQuestionDto(CheckPropertyDefinition property, WorkflowDefinition workflowDefinition)
        : base(property, workflowDefinition)
    {
    }
}

public record CurrencyQuestionDto : WeightedQuestionDto
{
    public CurrencyQuestionDto(CurrencyPropertyDefinition property, decimal totalWeight,
        WorkflowDefinition workflowDefinition) : base(property, totalWeight, workflowDefinition)
    {
    }
}

public record FileQuestionDto : QuestionDto
{
    public FileQuestionDto(FilePropertyDefinition property, WorkflowDefinition workflowDefinition) : base(property,
        workflowDefinition)
    {
        AllowedFileTypes = property.AllowedTypes;
        AllowedFileSize = property.MaximumSize;
    }

    public IReadOnlyList<string> AllowedFileTypes { get; init; }
    public int AllowedFileSize { get; init; }
}

public record UserQuestionDto : QuestionDto
{
    public UserQuestionDto(UserPropertyDefinition property, WorkflowDefinition workflowDefinition)
        : base(property, workflowDefinition)
    {
        AllowsExternalUsers = property.AllowsExternalUsers == true;
    }

    public bool AllowsExternalUsers { get; init; }
}

public record ChoiceQuestionDto : WeightedQuestionDto
{
    public ChoiceQuestionDto(ChoicePropertyDefinition property, decimal totalWeight,
        WorkflowDefinition workflowDefinition) : base(property, totalWeight, workflowDefinition)
    {
        Choices = (property.Values ?? []).Select(value => new ChoiceDto(
            value.Name, value.Text ?? value.Name, value.Description, value.Value)).ToArray();
        Control = property.Control;
        Rubric = property.Rubric?.Select(entry => RubricEntryDto.Create(entry, property.Values)).ToList();
        Sorting = property.Sorting;
    }

    public ChoiceDto[] Choices { get; init; }
    public ChoiceControl? Control { get; init; }
    public List<RubricEntryDto>? Rubric { get; init; }
    public ValueSetSorting? Sorting { get; init; }
}

public record ReferenceQuestionDto : QuestionDto
{
    public ReferenceQuestionDto(ReferencePropertyDefinition property, WorkflowDefinition workflowDefinition)
        : base(property, workflowDefinition)
    {
        WorkflowDefinition = property.WorkflowDefinition?.Name;
        Control = property.Control;
    }

    public string? WorkflowDefinition { get; init; }
    public ChoiceControl? Control { get; init; }
}

public record ObjectQuestionDto : QuestionDto
{
    public ObjectQuestionDto(ObjectPropertyDefinition property, decimal totalWeight,
        WorkflowDefinition workflowDefinition) : base(property, workflowDefinition)
    {
        WorkflowDefinition = property.WorkflowDefinition?.Name;
        Control = property.Control;
        SubProperties = property.WorkflowDefinition?.Properties
            .Select(child => Create(child, totalWeight, property.WorkflowDefinition!)).ToArray();
    }

    public string? WorkflowDefinition { get; init; }
    public ObjectControl? Control { get; init; }
    public QuestionDto[]? SubProperties { get; init; }
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