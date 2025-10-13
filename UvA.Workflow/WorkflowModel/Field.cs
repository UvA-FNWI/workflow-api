using UvA.Workflow.Expressions;

namespace UvA.Workflow.Entities.Domain;

public class Field
{
    public string? Property { get; set; }
    public string? Value { get; set; }
    public string? Href { get; set; }

    public BilingualString DisplayTitle =>
        Title ?? Question?.ShortDisplayName ?? Event?.Name ?? (CurrentStep ? "Step" : "Field");

    private string? ComputedProperty => CurrentStep ? "CurrentStep" : Property;

    private Template? _valueTemplate, _linkTemplate;
    public Template? ValueTemplate => _valueTemplate ??= Template.Create(Value);
    public Template? LinkTemplate => _linkTemplate ??= Template.Create(Href);
    private Expression? _propertyExpression;
    public Expression? PropertyExpression => _propertyExpression ??= ExpressionParser.Parse(ComputedProperty);
    public BilingualString? Title { get; set; } = null!;

    public string? Default { get; set; }

    [YamlIgnore] public Question? Question { get; set; }
    [YamlIgnore] public EventDefinition? Event { get; set; }


    public string? EndDate { get; set; }
    public bool CurrentStep { get; set; }

    public Lookup[] Properties =>
    [
        ..PropertyExpression?.Properties ?? [],
        ..ValueTemplate?.Properties ?? [],
        ..LinkTemplate?.Properties ?? [],
    ];
}