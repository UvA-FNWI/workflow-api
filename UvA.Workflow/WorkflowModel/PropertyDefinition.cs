using System.ComponentModel;
using UvA.Workflow.Expressions;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.WorkflowModel;

public enum PropertyVisibility
{
    Normal,
    Hidden
}

public enum ChoiceControl
{
    Dropdown,
    RadioList,
    Rubric,
    ComboBox
}

public enum ObjectControl
{
    InlineEditing,
    Modal
}

public enum StringControl
{
    TextArea,
    Email,
    Phone
}

/// <summary>
/// Defines a workflow property and the settings shared by all property types.
/// </summary>
public abstract class PropertyDefinition : INamed
{
    /// <summary>
    /// Internal name of the propertyDefinition
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized text of the propertyDefinition
    /// </summary>
    public BilingualString? Text { get; set; }

    public BilingualString DisplayName => Text ?? Name;
    public BilingualString ShortDisplayName => ShortText ?? Text ?? Name;

    /// <summary>
    /// Set if this propertyDefinition is hidden from users without extra permissions
    /// </summary>
    public PropertyVisibility Visibility { get; set; }

    /// <summary>
    /// Localized short propertyDefinition text to shown in results  
    /// </summary>
    public BilingualString? ShortText { get; set; }

    /// <summary>
    /// Data type of the propertyDefinition. Can be a primitive type String, Int, Double, DateTime, Date, User, Currency, File, Check
    /// or a reference to a value set or another entity type. Use [Type] to indicate an array and Type! to indicate
    /// a required value.
    /// </summary>
    public string Type { get; set; } = null!;

    /// <summary>
    /// Expression used to initialize this property when an instance is created without a value.
    /// </summary>
    public string? Default { get; set; }

    public Expression? DefaultExpression => ExpressionParser.Parse(Default);

    /// <summary>
    /// Expression for a read-only computed value. Computed values are not stored on the instance.
    /// </summary>
    public string? Expression { get; set; }

    [YamlIgnore] public bool IsComputed => Expression != null;
    [YamlIgnore] public Expression? ComputedExpression => ExpressionParser.Parse(Expression);

    public void EnsureWritable()
    {
        if (IsComputed)
            throw new InvalidWorkflowStateException("", "ComputedPropertyReadOnly",
                $"Computed property '{Name}' is read-only");
    }

    /// <summary>
    /// Localized extended description text for the propertyDefinition.
    /// </summary>
    public BilingualString? Description { get; set; }

    [YamlIgnore] public WorkflowDefinition ParentType { get; set; } = null!;

    public string UnderlyingType => Type.TrimEnd('!', ']').TrimStart('[');

    public bool IsRequired => Type.EndsWith('!');
    public bool IsArray => Type.StartsWith('[');

    public abstract DataType DataType { get; }

    /// <summary>
    /// Condition that determines if the propertyDefinition should be shown
    /// </summary>
    public Condition? Condition { get; set; }

    /// <summary>
    /// Condition that determines if the value is valid 
    /// </summary>
    public Condition? Validation { get; set; }

    public virtual BilingualString? GetValidationError(ObjectContext context)
        => Validation.IsMet(context)
            ? null
            : Validation?.Message ?? new BilingualString("Invalid value", "Ongeldige waarde");

    [YamlIgnore]
    public virtual IEnumerable<Condition> Conditions => new[] { Condition, Validation }.OfType<Condition>();

    public List<PropertyDefinition> DependentQuestions { get; } = [];

    /// <summary>
    /// Effect that is run whenever a value is changed for this property
    /// </summary>
    public Effect[] OnSave { get; set; } = [];

    /// <summary>
    /// Determines if the propertyDefinition should be hidden in the results table
    /// </summary>
    public bool HideInResults { get; set; }

    /// <summary>
    /// Settings for result display
    /// </summary>
    public ResultSettings? Results { get; set; }

    /// <summary>
    /// If set, this question is included in a form only when editing a matching property
    /// </summary>
    public string[]? Sources { get; set; }

    /// <summary>
    /// The name of another property this property is linked to.
    /// </summary>
    public string? LinkedTo { get; set; }
}

/// <summary>Property with a numeric value that can contribute to assessment calculations.</summary>
public abstract class WeightedPropertyDefinition : PropertyDefinition
{
    /// <summary>
    /// Settings for result calculation
    /// </summary>
    public CalculationSettings? Calculation { get; set; }
}

/// <summary>Text value with optional input control and length validation.</summary>
public class StringPropertyDefinition : PropertyDefinition
{
    public override DataType DataType => DataType.String;

    /// <summary>Render a text area, email or phone input; omitted for ordinary text.</summary>
    public StringControl? Control { get; set; }
}

/// <summary>Calendar date, also used for step deadlines and postponement limits.</summary>
public class DatePropertyDefinition : PropertyDefinition
{
    public override DataType DataType => DataType.Date;
}

/// <summary>Date and time value.</summary>
public class DateTimePropertyDefinition : PropertyDefinition
{
    public override DataType DataType => DataType.DateTime;
}

/// <summary>Whole number without a fractional part.</summary>
public class IntPropertyDefinition : WeightedPropertyDefinition
{
    public override DataType DataType => DataType.Int;
}

/// <summary>Number that can include a fractional part.</summary>
public class DoublePropertyDefinition : WeightedPropertyDefinition
{
    public override DataType DataType => DataType.Double;
}

/// <summary>Boolean value, such as a yes/no answer.</summary>
public class CheckPropertyDefinition : PropertyDefinition
{
    public override DataType DataType => DataType.Check;
}

/// <summary>Monetary amount paired with a currency code.</summary>
public class CurrencyPropertyDefinition : WeightedPropertyDefinition
{
    public override DataType DataType => DataType.Currency;
}

/// <summary>Uploaded file with configurable allowed file types and size limits.</summary>
public class FilePropertyDefinition : PropertyDefinition
{
    private const int DefaultMaximumSize = 10_000_000;

    public override DataType DataType => DataType.File;

    /// <summary>
    /// Prefix (template) to add to the file names when storing files
    /// </summary>
    public string? Prefix { get; set; }

    public Template? PrefixTemplate => Template.Create(Prefix);

    /// <summary>
    /// File extensions that may be uploaded for a File propertyDefinition (for example pdf or zip).
    /// Use * to allow any format.
    /// Defaults to pdf when omitted.
    /// </summary>
    public string[] AllowedTypes { get; set; } = ["pdf"];

    /// <summary>
    /// Maximum file size in bytes for a File propertyDefinition. Defaults to 10000000 (10 MB) when omitted.
    /// </summary>
    [DefaultValue(DefaultMaximumSize)]
    public int MaximumSize { get; set; } = DefaultMaximumSize;
}

/// <summary>User account selection, optionally allowing external users.</summary>
public class UserPropertyDefinition : PropertyDefinition
{
    public override DataType DataType => DataType.User;

    /// <summary>
    /// Determines if the propertyDefinition allows external users
    /// </summary>
    public bool? AllowsExternalUsers { get; set; } = false;
}

/// <summary>Selection from inline choices or a named value set, with optional control and rubric settings.</summary>
public class ChoicePropertyDefinition : WeightedPropertyDefinition
{
    public override DataType DataType => DataType.Choice;

    /// <summary>Values for a choice property.</summary>
    public List<Choice>? Values { get; set; }

    /// <summary>Sorting inherited from the referenced value set.</summary>
    [YamlIgnore]
    public ValueSetSorting? Sorting { get; set; }

    /// <summary>Control for choice input; omitted to use the default control.</summary>
    public ChoiceControl? Control { get; set; }

    /// <summary>Rubric entries that describe grading criteria for this property.</summary>
    public List<RubricEntry>? Rubric { get; set; }

    public override IEnumerable<Condition> Conditions => base.Conditions
        .Concat((Values ?? []).Select(v => v.Condition).OfType<Condition>());
}

/// <summary>Property whose value follows another workflow definition.</summary>
public abstract class WorkflowPropertyDefinition : PropertyDefinition
{
    [YamlIgnore] public WorkflowDefinition? WorkflowDefinition { get; set; }
}

/// <summary>Reference to an existing instance of a named workflow definition.</summary>
public class ReferencePropertyDefinition : WorkflowPropertyDefinition
{
    public override DataType DataType => DataType.Reference;

    /// <summary>Control for reference selection; omitted to use the default control.</summary>
    public ChoiceControl? Control { get; set; }

    /// <summary>Roles inherited from the referenced instance.</summary>
    public string[] InheritedRoles { get; set; } = [];

    /// <summary>Condition used for filtering reference choices.</summary>
    public Condition? Filter { get; set; }

    public override IEnumerable<Condition> Conditions => base.Conditions
        .Concat(new[] { Filter }.OfType<Condition>());
}

/// <summary>Nested property values defined by a named embedded workflow definition.</summary>
public class ObjectPropertyDefinition : WorkflowPropertyDefinition
{
    public override DataType DataType => DataType.Object;

    /// <summary>Control for embedded objects; omitted to use the default control.</summary>
    public ObjectControl? Control { get; set; }
}

public enum CalculationType
{
    Average,
    Sum,
}

public class CalculationSettings
{
    /// <summary>
    /// The weight of this field
    /// </summary>
    public decimal? Weight { get; set; }

    /// <summary>
    /// Determines how this field is used in the calculation. For Average, set a weight to use it in a weighted average.
    /// For Sum, the weight is ignored.
    /// </summary>
    public CalculationType Type { get; set; }
}

public class ResultSettings
{
    /// <summary>
    /// Determines what kind of results are shown for this field
    /// </summary>
    public ResultType Type { get; set; }

    /// <summary>
    /// For Type = Source, this determines which source property to use
    /// </summary>
    public string? Source { get; set; }
}

public enum ResultType
{
    Source,
    Average
}

public class Choice : INamed
{
    /// <summary>
    /// Internal name of the choice
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized text of the choice
    /// </summary>
    public BilingualString? Text { get; set; }

    /// <summary>
    /// Localized extended description text for the choice
    /// </summary>
    public BilingualString? Description { get; set; }

    /// <summary>
    /// Numeric value of the choice
    /// </summary>
    public double? Value { get; set; }

    /// <summary>
    /// Condition that determines if the choice should be shown
    /// </summary>
    public Condition? Condition { get; set; }

    public static implicit operator Choice(string value) => new Choice { Text = value };
}

public class RubricEntry : INamed
{
    public string Name { get; set; } = null!;

    public required BilingualString Description { get; set; }

    public required List<string> Grades { get; set; }
}