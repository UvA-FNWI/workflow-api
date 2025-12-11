using UvA.Workflow.Expressions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Entities.Domain;

public class Field
{
    /// <summary>
    /// Property this field refers to
    /// </summary>
    public string? Property { get; set; }

    /// <summary>
    /// Custom value template
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Url the field should refer to
    /// </summary>
    public string? Href { get; set; }

    /// <summary>
    /// Default value to use if the target is empty
    /// </summary>
    public string? Default { get; set; }

    public BilingualString DisplayTitle =>
        Title ?? PropertyDefinition?.ShortDisplayName ?? Event?.Name ?? (CurrentStep ? "Step" : "Field");

    private string? ComputedProperty => CurrentStep ? "CurrentStep" : Property;

    private Template? _valueTemplate, _linkTemplate;
    public Template? ValueTemplate => _valueTemplate ??= Template.Create(Value);
    public Template? LinkTemplate => _linkTemplate ??= Template.Create(Href);
    private Expression? _propertyExpression;
    public Expression? PropertyExpression => _propertyExpression ??= ExpressionParser.Parse(ComputedProperty);
    public BilingualString? Title { get; set; } = null!;


    [YamlIgnore] public PropertyDefinition? PropertyDefinition { get; set; }
    [YamlIgnore] public EventDefinition? Event { get; set; }


    /// <summary>
    /// Target step the field should show the end date of
    /// </summary>
    public string? EndDate { get; set; }

    /// <summary>
    /// If true, display the current step in this field
    /// </summary>
    public bool CurrentStep { get; set; }

    public Lookup[] Properties =>
    [
        ..PropertyExpression?.Properties ?? [],
        ..ValueTemplate?.Properties ?? [],
        ..LinkTemplate?.Properties ?? [],
    ];
}