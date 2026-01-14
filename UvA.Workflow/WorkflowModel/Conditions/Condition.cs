using System.Collections;
using UvA.Workflow.Expressions;

namespace UvA.Workflow.Entities.Domain.Conditions;

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
    /// For and/or constructs
    /// </summary>
    public Logical? Logical { get; set; }

    /// <summary>
    /// Check if an event has occurred
    /// </summary>
    public EventCondition? Event { get; set; }

    /// <summary>
    /// Check if a date has passed
    /// </summary>
    public Date? Date { get; set; }

    /// <summary>
    /// Check if a deadline has passed
    /// </summary>
    public Deadline? Deadline { get; set; }

    /// <summary>
    /// Use a named reusable condition
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Message that is shown then the condition is not, for use in form validation
    /// </summary>
    public BilingualString? Message { get; set; }

    [JsonIgnore] [YamlIgnore] public Condition? NamedCondition { get; set; }

    public ConditionPart Part => Value ?? Logical ?? Date ?? Deadline ?? Event ?? NamedCondition?.Part!;

    public IEnumerable<Lookup> Properties => Part?.Properties ?? [];

    public static implicit operator Condition(string name) => new() { Name = name };
}

public abstract class ConditionPart
{
    public virtual Lookup[] Dependants => [];
    public abstract bool IsMet(ObjectContext context);
    public virtual IEnumerable<Lookup> Properties => [];
}

public class Date : ConditionPart
{
    public string Source { get; set; } = null!;

    public override IEnumerable<Lookup> Properties => [Source];

    public override bool IsMet(ObjectContext context)
    {
        var date = context.Get(Source) as DateTime?;
        return date != null && date.Value <= DateTime.Now;
    }

    public static implicit operator Date(string s) => new Date { Source = s };
}

public class Deadline : ConditionPart
{
    public string ExpressionText { get; set; } = null!;

    private Expression Expression => ExpressionParser.Parse(ExpressionText);

    public DateTime? Evaluate(ObjectContext context)
        => Expression.Execute(context) switch
        {
            DateTime d => d,
            string s => DateTime.Parse(s),
            _ => null
        };

    public override IEnumerable<Lookup> Properties => Expression.Properties;

    public override bool IsMet(ObjectContext context)
    {
        var deadline = Evaluate(context);
        return deadline != null && deadline.Value > DateTime.Now;
    }

    public static implicit operator Deadline(string s) => new() { ExpressionText = s };
}

public class EventCondition : ConditionPart
{
    /// <summary>
    /// Id of the event to check for
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>
    /// If set, the event specified by Id must have occurred on or after the event specified by this property
    /// </summary>
    public string? NotBefore { get; set; } = null!;

    public override bool IsMet(ObjectContext context)
    {
        var date = context.Get(Id + "Event") as DateTime?;
        var notBeforeDate = context.Get(NotBefore + "Event") as DateTime?;
        return date != null && (notBeforeDate == null || date >= notBeforeDate);
    }

    public static implicit operator EventCondition(string s) => new() { Id = s };
}

public enum LogicalOperator
{
    And,
    Or
}

public class Logical : ConditionPart
{
    public LogicalOperator Operator { get; set; }
    public Condition[] Children { get; set; } = null!;

    public override Lookup[] Dependants => Children.SelectMany(c => c.Part.Dependants).ToArray();
    public override IEnumerable<Lookup> Properties => Children.SelectMany(c => c.Part.Properties);

    public override bool IsMet(ObjectContext context)
        => Operator switch
        {
            LogicalOperator.And => Children.All(c => c.IsMet(context)),
            LogicalOperator.Or => Children.Any(c => c.IsMet(context)),
            _ => throw new NotImplementedException()
        };
}

/// <summary>
/// Represents a value comparison. Use this to compare two properties, or to compare a property to a fixed literal value
/// (prefix the value with an equals sign to indicate a literal value)
/// </summary>
public class Value : ConditionPart
{
    /// <summary>
    /// Target property
    /// </summary>
    public string Property { get; set; } = null!;

    private Expression PropertyExpression => ExpressionParser.Parse(Property);

    /// <summary>
    /// Value the property should be equal to. If this is a literal value, it must be prefixed with an equals sign 
    /// </summary>
    public string? Equal { get; set; }

    private Expression? EqualExpression => ExpressionParser.Parse(Equal);

    /// <summary>
    /// Value the property should be less than. If this is a literal value, it must be prefixed with an equals sign 
    /// </summary>
    public string? LessThan { get; set; }

    private Expression? LessThanExpression => ExpressionParser.Parse(LessThan);

    /// <summary>
    /// Value the property should be greater than. If this is a literal value, it must be prefixed with an equals sign 
    /// </summary>
    public string? GreaterThan { get; set; }

    private Expression? GreaterThanExpression => ExpressionParser.Parse(GreaterThan);

    /// <summary>
    /// Value the property should be greater than or equal to. If this is a literal value, it must be prefixed with an equals sign 
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

    public override Lookup[] Dependants =>
    [
        Property,
        ..EqualExpression?.Properties ?? [],
        ..LessThanExpression?.Properties ?? [],
        ..GreaterThanExpression?.Properties ?? [],
        ..GreaterThanOrEqualExpression?.Properties ?? [],
        ..InExpression?.Properties ?? []
    ];

    public override IEnumerable<Lookup> Properties => CollectionTools.Merge(PropertyExpression.Properties,
        EqualExpression?.Properties, LessThanExpression?.Properties, GreaterThanExpression?.Properties,
        InExpression?.Properties);

    public override bool IsMet(ObjectContext context)
    {
        var prop = PropertyExpression.Execute(context);
        if (EqualExpression != null && prop is string[] array)
            return array.Length == 1 && Equals(EqualExpression.Execute(context), array[0]);
        if (EqualExpression != null)
            return Equals(EqualExpression.Execute(context), prop);
        if (LessThanExpression != null)
            return (prop as IComparable)?.CompareTo(LessThanExpression.Execute(context)) < 0;
        if (GreaterThanExpression != null)
            return (prop as IComparable)?.CompareTo(GreaterThanExpression.Execute(context)) > 0;
        if (GreaterThanOrEqualExpression != null)
            return (prop as IComparable)?.CompareTo(GreaterThanOrEqualExpression.Execute(context)) >= 0;
        if (IsEmpty != null)
            return IsEmpty.Value ^ !string.IsNullOrWhiteSpace(prop?.ToString());
        if (InExpression != null)
            return InExpression.Execute(context) is IEnumerable p && p.Cast<object>().Contains(prop);
        throw new InvalidOperationException("Invalid condition");
    }
}