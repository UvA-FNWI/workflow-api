using System.Collections;
using System.Globalization;
using UvA.Workflow.Expressions;

namespace UvA.Workflow.WorkflowModel.Conditions;

/// <summary>
/// Represents a logical condition
/// </summary>
public class Condition
{
    /// <summary>
    /// If true, this condition is inverted
    /// </summary>
    public bool Not { get; set; }

    /// <summary>
    /// Compare property values
    /// </summary>
    public Value? Value { get; set; }

    /// <summary>
    /// Require all of these conditions to be met
    /// </summary>
    public And? And
    {
        get;
        set
        {
            if (value != null && Or != null)
                throw new InvalidOperationException("A condition cannot have both 'and' and 'or'.");
            field = value;
        }
    }

    /// <summary>
    /// Require at least one of these conditions to be met
    /// </summary>
    public Or? Or
    {
        get;
        set
        {
            if (value != null && And != null)
                throw new InvalidOperationException("A condition cannot have both 'and' and 'or'.");
            field = value;
        }
    }

    /// <summary>
    /// Check if an event has occurred
    /// </summary>
    public EventCondition? Event { get; set; }

    /// <summary>
    /// Check if a date has passed
    /// </summary>
    public Date? Date { get; set; }

    /// <summary>
    /// Check if a deadline has not yet passed. Use a step's deadline setting for soft/hard submission deadlines.
    /// </summary>
    public DeadlineCondition? Deadline { get; set; }

    /// <summary>
    /// Use a named reusable condition
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Message that is shown then the condition is not, for use in form validation
    /// </summary>
    public BilingualString? Message { get; set; }

    [JsonIgnore] [YamlIgnore] public BilingualTemplate? MessageTemplate => field ??= BilingualTemplate.Create(Message);

    [JsonIgnore] [YamlIgnore] public Condition? NamedCondition { get; set; }

    internal CompositeCondition? Composite => And ?? (CompositeCondition?)Or;
    internal IConditionPart? LocalPart => Value ?? Composite ?? Date ?? Deadline ?? (IConditionPart?)Event;
    public IConditionPart? Part => LocalPart ?? NamedCondition?.Part;

    public IEnumerable<Lookup> Properties => Part?.Properties ?? [];
    internal Lookup[] Dependants => Part?.Dependants ?? [];

    [JsonIgnore]
    [YamlIgnore]
    public IEnumerable<Lookup> MessageLookups =>
        MessageTemplate?.Properties ??
        (Part is Or alternatives
            ? alternatives.SelectMany(branch => branch.MessageTemplate?.Properties ?? [])
            : []);

    public static implicit operator Condition(string name) => new() { Name = name };
}

public interface IConditionPart
{
    Lookup[] Dependants => [];
    bool IsMet(ObjectContext context);

    IEnumerable<Lookup> Properties => [];
}

public class Date : IConditionPart
{
    public string Source { get; set; } = null!;

    public IEnumerable<Lookup> Properties => [Source];

    public bool IsMet(ObjectContext context)
    {
        var date = context.Get(Source) as DateTime?;
        return date != null && date.Value.ToUniversalTime() <= context.Now.ToUniversalTime();
    }

    public static implicit operator Date(string s) => new Date { Source = s };
}

public class DeadlineCondition : IConditionPart
{
    public string ExpressionText { get; set; } = null!;

    private Expression Expression => ExpressionParser.Parse(ExpressionText);

    public DateTimeOffset? Evaluate(ObjectContext context)
        => Expression.Execute(context) switch
        {
            DateTimeOffset d => d,
            DateTime d => ToDateTimeOffset(d),
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal),
            _ => null
        };

    private static DateTimeOffset ToDateTimeOffset(DateTime date)
    {
        if (date.Kind is DateTimeKind.Utc or DateTimeKind.Local)
            return new DateTimeOffset(date);

        var serverTimezoneOffset = TimeZoneInfo.Local.GetUtcOffset(date);
        return new DateTimeOffset(date, serverTimezoneOffset);
    }

    public IEnumerable<Lookup> Properties => Expression.Properties;

    public bool IsMet(ObjectContext context)
    {
        var deadline = Evaluate(context);
        return deadline != null && deadline.Value.UtcDateTime > context.Now.ToUniversalTime();
    }

    public static implicit operator DeadlineCondition(string s) => new() { ExpressionText = s };
}

public class EventCondition : IConditionPart
{
    /// <summary>
    /// Id of the event to check for
    /// </summary>
    public string Id { get; set; } = null!;

    public bool IsMet(ObjectContext context)
    {
        // Check if event exists and is active (not suppressed)
        var isActive = context.Get(Id + "EventActive") as bool? ?? false;
        if (!isActive)
            return false;

        var date = context.Get(Id + "Event") as DateTime?;
        if (date == null)
            return false;

        return true;
    }

    public static implicit operator EventCondition(string s) => new() { Id = s };
}

/// <summary>
/// Represents a value comparison. Use this to compare two properties, or to compare a property to a fixed literal value
/// (prefix a string value with an equals sign to indicate a literal value)
/// </summary>
public class Value : IConditionPart
{
    /// <summary>
    /// Target property
    /// </summary>
    public string Property { get; set; } = null!;

    private Expression PropertyExpression => ExpressionParser.Parse(Property);

    /// <summary>
    /// Value the property should be equal to. If this is a literal string, it must be prefixed with an equals sign 
    /// </summary>
    public string? Equal { get; set; }

    private Expression? EqualExpression => ExpressionParser.Parse(Equal);

    /// <summary>
    /// Value the property should be less than 
    /// </summary>
    public string? LessThan { get; set; }

    private Expression? LessThanExpression => ExpressionParser.Parse(LessThan);

    /// <summary>
    /// Value the property should be less than or equal to
    /// </summary>
    public string? LessThanOrEqual { get; set; }

    private Expression? LessThanOrEqualExpression => ExpressionParser.Parse(LessThanOrEqual);

    /// <summary>
    /// Value the property should be greater than 
    /// </summary>
    public string? GreaterThan { get; set; }

    private Expression? GreaterThanExpression => ExpressionParser.Parse(GreaterThan);

    /// <summary>
    /// Value the property should be greater than or equal to 
    /// </summary>
    public string? GreaterThanOrEqual { get; set; }

    /// <summary>
    /// Array the property should be in 
    /// </summary>
    public string? In { get; set; }

    private Expression? InExpression => ExpressionParser.Parse(In);

    /// <summary>
    /// If set, check whether the property is empty 
    /// </summary>
    public bool? IsEmpty { get; set; }

    private Expression? GreaterThanOrEqualExpression => ExpressionParser.Parse(GreaterThanOrEqual);

    /// <summary>
    /// Maximum length of a string
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Minimum length of a string
    /// </summary>
    public int? MinLength { get; set; }

    public Lookup[] Dependants =>
    [
        .. PropertyExpression.Properties,
        .. EqualExpression?.Properties ?? [],
        .. LessThanExpression?.Properties ?? [],
        .. LessThanOrEqualExpression?.Properties ?? [],
        .. GreaterThanExpression?.Properties ?? [],
        .. GreaterThanOrEqualExpression?.Properties ?? [],
        .. InExpression?.Properties ?? []
    ];

    public IEnumerable<Lookup> Properties => CollectionTools.Merge(PropertyExpression.Properties,
        EqualExpression?.Properties, LessThanExpression?.Properties, LessThanOrEqualExpression?.Properties,
        GreaterThanExpression?.Properties,
        GreaterThanOrEqualExpression?.Properties, InExpression?.Properties);

    public bool IsMet(ObjectContext context)
    {
        var prop = PropertyExpression.Execute(context);
        if (EqualExpression != null && prop is string[] array)
            return array.Length == 1 && Equals(EqualExpression.Execute(context), array[0]);
        if (EqualExpression != null)
            return Equals(EqualExpression.Execute(context), prop);
        if (LessThanExpression != null)
            return Compare(prop, LessThanExpression.Execute(context)) < 0;
        if (LessThanOrEqualExpression != null)
            return Compare(prop, LessThanOrEqualExpression.Execute(context)) <= 0;
        if (GreaterThanExpression != null)
            return Compare(prop, GreaterThanExpression.Execute(context)) > 0;
        if (GreaterThanOrEqualExpression != null)
            return Compare(prop, GreaterThanOrEqualExpression.Execute(context)) >= 0;
        if (IsEmpty != null)
            return Equals(IsEmpty.Value, DataExtensions.IsEffectivelyEmpty(prop));
        if (InExpression != null)
            return InExpression.Execute(context) is IEnumerable p && p.Cast<object>().Contains(prop);
        if (MaxLength != null)
            return prop is string s && s.Length <= MaxLength;
        if (MinLength != null)
            return prop is string s && s.Length >= MinLength;
        throw new InvalidOperationException("Invalid condition");
    }

    private static int? Compare(object? left, object? right)
    {
        if (IsNumber(left) && IsNumber(right))
            return left is float or double || right is float or double
                ? Convert.ToDouble(left).CompareTo(Convert.ToDouble(right))
                : Convert.ToDecimal(left).CompareTo(Convert.ToDecimal(right));
        return (left as IComparable)?.CompareTo(right);
    }

    private static bool IsNumber(object? value)
        => value is int or long or float or double or decimal;
}