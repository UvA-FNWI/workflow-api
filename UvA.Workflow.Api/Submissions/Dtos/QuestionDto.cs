using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public abstract record QuestionDto
{
    protected QuestionDto(PropertyDefinition property)
    {
        Id = $"{property.ParentType.Name}_{property.Name}";
        Name = property.Name;
        Text = property.DisplayName;
        IsRequired = property.IsRequired;
        IsArray = property.IsArray;
        Description = property.Description;
        ShortText = property.ShortDisplayName;
        HideInResults = property.HideInResults;
        LinkedTo = property.LinkedTo;
    }

    public string Id { get; init; }
    public string Name { get; init; }
    public BilingualString Text { get; init; }
    public abstract DataType Type { get; }
    public bool IsRequired { get; init; }
    public bool IsArray { get; init; }
    public BilingualString? Description { get; init; }
    public BilingualString? ShortText { get; init; }
    public bool HideInResults { get; init; }
    public string? LinkedTo { get; init; }

    public static QuestionDto Create(PropertyDefinition property, decimal totalWeight)
        => property switch
        {
            StringPropertyDefinition text => new StringQuestionDto(text),
            DatePropertyDefinition date => new DateQuestionDto(date),
            DateTimePropertyDefinition dateTime => new DateTimeQuestionDto(dateTime),
            IntPropertyDefinition integer => new IntQuestionDto(integer, totalWeight),
            DoublePropertyDefinition number => new DoubleQuestionDto(number, totalWeight),
            CheckPropertyDefinition check => new CheckQuestionDto(check),
            CurrencyPropertyDefinition currency => new CurrencyQuestionDto(currency, totalWeight),
            FilePropertyDefinition file => new FileQuestionDto(file),
            UserPropertyDefinition user => new UserQuestionDto(user),
            ChoicePropertyDefinition choice => new ChoiceQuestionDto(choice, totalWeight),
            ReferencePropertyDefinition reference => new ReferenceQuestionDto(reference),
            ObjectPropertyDefinition embedded => new ObjectQuestionDto(embedded, totalWeight),
            _ => throw new ArgumentException($"Unsupported property type: {property.GetType().Name}")
        };
}

public abstract record WeightedQuestionDto : QuestionDto
{
    protected WeightedQuestionDto(WeightedPropertyDefinition property, decimal totalWeight) : base(property)
    {
        Weight = property.Calculation?.Weight;
        Percentage = totalWeight == 0 || Weight == null ? null : Weight / totalWeight * 100;
    }

    public decimal? Weight { get; init; }
    public decimal? Percentage { get; init; }
}

public record StringQuestionDto : QuestionDto
{
    public StringQuestionDto(StringPropertyDefinition property) : base(property)
    {
        Layout = property.Layout;
        MaxLength = property.Validation.GetAllValues().Select(v => v.MaxLength).FirstOrDefault(v => v != null);
        MinLength = property.Validation.GetAllValues().Select(v => v.MinLength).FirstOrDefault(v => v != null);
    }

    public override DataType Type => DataType.String;
    public TextLayoutOptions? Layout { get; init; }
    public int? MaxLength { get; init; }
    public int? MinLength { get; init; }
}

public record DateQuestionDto : QuestionDto
{
    public DateQuestionDto(DatePropertyDefinition property) : base(property)
    {
    }

    public override DataType Type => DataType.Date;
}

public record DateTimeQuestionDto : QuestionDto
{
    public DateTimeQuestionDto(DateTimePropertyDefinition property) : base(property)
    {
    }

    public override DataType Type => DataType.DateTime;
}

public record IntQuestionDto : WeightedQuestionDto
{
    public IntQuestionDto(IntPropertyDefinition property, decimal totalWeight) : base(property, totalWeight)
    {
    }

    public override DataType Type => DataType.Int;
}

public record DoubleQuestionDto : WeightedQuestionDto
{
    public DoubleQuestionDto(DoublePropertyDefinition property, decimal totalWeight) : base(property, totalWeight)
    {
    }

    public override DataType Type => DataType.Double;
}

public record CheckQuestionDto : QuestionDto
{
    public CheckQuestionDto(CheckPropertyDefinition property) : base(property)
    {
    }

    public override DataType Type => DataType.Check;
}

public record CurrencyQuestionDto : WeightedQuestionDto
{
    public CurrencyQuestionDto(CurrencyPropertyDefinition property, decimal totalWeight) : base(property, totalWeight)
    {
    }

    public override DataType Type => DataType.Currency;
}

public record FileQuestionDto : QuestionDto
{
    public FileQuestionDto(FilePropertyDefinition property) : base(property)
    {
        AllowedFileTypes = property.EffectiveAllowedFileTypes;
        AllowedFileSize = property.EffectiveAllowedFileSize;
    }

    public override DataType Type => DataType.File;
    public IReadOnlyList<string> AllowedFileTypes { get; init; }
    public int AllowedFileSize { get; init; }
}

public record UserQuestionDto : QuestionDto
{
    public UserQuestionDto(UserPropertyDefinition property) : base(property)
    {
        AllowsExternalUsers = property.AllowsExternalUsers == true;
    }

    public override DataType Type => DataType.User;
    public bool AllowsExternalUsers { get; init; }
}

public record ChoiceQuestionDto : WeightedQuestionDto
{
    public ChoiceQuestionDto(ChoicePropertyDefinition property, decimal totalWeight) : base(property, totalWeight)
    {
        Choices = (property.Values ?? []).Select(value => new ChoiceDto(
            value.Name, value.Text ?? value.Name, value.Description, value.Value)).ToArray();
        Layout = property.Layout;
        Rubric = property.Rubric?.Select(entry => RubricEntryDto.Create(entry, property.Values)).ToList();
        Sorting = property.Sorting;
    }

    public override DataType Type => DataType.Choice;
    public ChoiceDto[] Choices { get; init; }
    public ChoiceLayoutOptions? Layout { get; init; }
    public List<RubricEntryDto>? Rubric { get; init; }
    public ValueSetSorting? Sorting { get; init; }
}

public record ReferenceQuestionDto : QuestionDto
{
    public ReferenceQuestionDto(ReferencePropertyDefinition property) : base(property)
    {
        WorkflowDefinition = property.WorkflowDefinition?.Name;
        Layout = property.Layout;
    }

    public override DataType Type => DataType.Reference;
    public string? WorkflowDefinition { get; init; }
    public ChoiceLayoutOptions? Layout { get; init; }
}

public record ObjectQuestionDto : QuestionDto
{
    public ObjectQuestionDto(ObjectPropertyDefinition property, decimal totalWeight) : base(property)
    {
        WorkflowDefinition = property.WorkflowDefinition?.Name;
        Layout = property.Layout;
        SubProperties = property.WorkflowDefinition?.Properties.Select(child => Create(child, totalWeight)).ToArray();
    }

    public override DataType Type => DataType.Object;
    public string? WorkflowDefinition { get; init; }
    public TableLayoutOptions? Layout { get; init; }
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