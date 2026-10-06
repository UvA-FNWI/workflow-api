using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.Submissions.Dtos;

public abstract record QuestionDto
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public BilingualString Text { get; init; } = null!;
    public abstract DataType Type { get; }
    public bool IsRequired { get; init; }
    public bool IsArray { get; init; }
    public BilingualString? Description { get; init; }
    public BilingualString? ShortText { get; init; }
    public bool HideInResults { get; init; }
    public decimal? Weight { get; init; }
    public decimal? Percentage { get; init; }
    public string? LinkedTo { get; init; }

    public static QuestionDto Create(PropertyDefinition property, ObjectContext context,
        decimal totalWeight, WorkflowDefinition? definition = null)
    {
        QuestionDto question = property switch
        {
            StringPropertyDefinition text => new StringQuestionDto
            {
                Layout = text.Layout,
                MaxLength = text.Validation.GetAllValues().Select(v => v.MaxLength).FirstOrDefault(v => v != null),
                MinLength = text.Validation.GetAllValues().Select(v => v.MinLength).FirstOrDefault(v => v != null)
            },
            DatePropertyDefinition => new DateQuestionDto(),
            DateTimePropertyDefinition => new DateTimeQuestionDto(),
            IntPropertyDefinition => new IntQuestionDto(),
            DoublePropertyDefinition => new DoubleQuestionDto(),
            CheckPropertyDefinition => new CheckQuestionDto(),
            CurrencyPropertyDefinition => new CurrencyQuestionDto(),
            FilePropertyDefinition file => new FileQuestionDto
            {
                AllowedFileTypes = file.EffectiveAllowedFileTypes,
                AllowedFileSize = file.EffectiveAllowedFileSize
            },
            UserPropertyDefinition user => new UserQuestionDto
                { AllowsExternalUsers = user.AllowsExternalUsers == true },
            ChoicePropertyDefinition choice => new ChoiceQuestionDto
            {
                Choices = (choice.Values ?? []).Select(value => new ChoiceDto(
                    value.Name, value.Text ?? value.Name, value.Description, value.Value)).ToArray(),
                Layout = choice.Layout,
                Rubric = choice.Rubric?.Select(entry => RubricEntryDto.Create(entry, choice.Values)).ToList(),
                Sorting = choice.Sorting
            },
            ReferencePropertyDefinition reference => new ReferenceQuestionDto
            {
                WorkflowDefinition = reference.WorkflowDefinition?.Name,
                Layout = reference.Layout
            },
            ObjectPropertyDefinition embedded => new ObjectQuestionDto
            {
                WorkflowDefinition = embedded.WorkflowDefinition?.Name,
                Layout = embedded.Layout,
                SubProperties = embedded.WorkflowDefinition?.Properties
                    .Select(child => Create(child, context, totalWeight)).ToArray()
            },
            _ => throw new ArgumentException($"Unsupported property type: {property.GetType().Name}")
        };

        var weight = property.Calculation?.Weight;
        return question with
        {
            Id = $"{property.ParentType.Name}_{property.Name}",
            Name = property.Name,
            Text = property.DisplayName,
            IsRequired = property.IsRequired,
            IsArray = property.IsArray,
            Description = property.Description,
            ShortText = property.ShortDisplayName,
            HideInResults = property.HideInResults,
            Weight = weight,
            Percentage = totalWeight == 0 || weight == null ? null : weight / totalWeight * 100,
            LinkedTo = property.LinkedTo
        };
    }
}

public record StringQuestionDto : QuestionDto
{
    public override DataType Type => DataType.String;
    public TextLayoutOptions? Layout { get; init; }
    public int? MaxLength { get; init; }
    public int? MinLength { get; init; }
}

public record DateQuestionDto : QuestionDto
{
    public override DataType Type => DataType.Date;
}

public record DateTimeQuestionDto : QuestionDto
{
    public override DataType Type => DataType.DateTime;
}

public record IntQuestionDto : QuestionDto
{
    public override DataType Type => DataType.Int;
}

public record DoubleQuestionDto : QuestionDto
{
    public override DataType Type => DataType.Double;
}

public record CheckQuestionDto : QuestionDto
{
    public override DataType Type => DataType.Check;
}

public record CurrencyQuestionDto : QuestionDto
{
    public override DataType Type => DataType.Currency;
}

public record FileQuestionDto : QuestionDto
{
    public override DataType Type => DataType.File;
    public IReadOnlyList<string> AllowedFileTypes { get; init; } = [];
    public int AllowedFileSize { get; init; }
}

public record UserQuestionDto : QuestionDto
{
    public override DataType Type => DataType.User;
    public bool AllowsExternalUsers { get; init; }
}

public record ChoiceQuestionDto : QuestionDto
{
    public override DataType Type => DataType.Choice;
    public ChoiceDto[] Choices { get; init; } = [];
    public ChoiceLayoutOptions? Layout { get; init; }
    public List<RubricEntryDto>? Rubric { get; init; }
    public ValueSetSorting? Sorting { get; init; }
}

public record ReferenceQuestionDto : QuestionDto
{
    public override DataType Type => DataType.Reference;
    public string? WorkflowDefinition { get; init; }
    public ChoiceLayoutOptions? Layout { get; init; }
}

public record ObjectQuestionDto : QuestionDto
{
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