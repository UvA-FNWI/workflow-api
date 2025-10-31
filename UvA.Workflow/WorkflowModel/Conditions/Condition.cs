using UvA.Workflow.Expressions;

namespace UvA.Workflow.Entities.Domain.Conditions;

public class Condition
{
    public bool Not { get; set; }
    public Value? Value { get; set; }
    public Logical? Logical { get; set; }
    public EventCondition? Event { get; set; }
    public Date? Date { get; set; }
    public string? Name { get; set; }

    public BilingualString? Message { get; set; }

    [JsonIgnore] [YamlIgnore] public Condition? NamedCondition { get; set; }

    public ConditionPart Part => Value ?? Logical ?? Date ?? Event ?? NamedCondition?.Part!;

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

public class EventCondition : ConditionPart
{
    /// <summary>
    /// Id of the event to check for
    /// </summary>
    public string Id { get; set; } = null!;

    public override bool IsMet(ObjectContext context)
        => context.Get(Id + "Event") != null;

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

public class Value : ConditionPart
{
    public string Property { get; set; } = null!;
    private Expression PropertyExpression => ExpressionParser.Parse(Property);
    public string? Equal { get; set; }
    private Expression? EqualExpression => ExpressionParser.Parse(Equal);
    public string? LessThan { get; set; }
    private Expression? LessThanExpression => ExpressionParser.Parse(LessThan);
    public string? GreaterThan { get; set; }
    private Expression? GreaterThanExpression => ExpressionParser.Parse(GreaterThan);
    public string? GreaterThanOrEqual { get; set; }
    private Expression? GreaterThanOrEqualExpression => ExpressionParser.Parse(GreaterThanOrEqual);

    public override Lookup[] Dependants =>
    [
        Property,
        ..EqualExpression?.Properties ?? [],
        ..LessThanExpression?.Properties ?? [],
        ..GreaterThanExpression?.Properties ?? [],
        ..GreaterThanOrEqualExpression?.Properties ?? [],
    ];

    public override IEnumerable<Lookup> Properties => CollectionTools.Merge(PropertyExpression.Properties,
        EqualExpression?.Properties, LessThanExpression?.Properties, GreaterThanExpression?.Properties);

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
        throw new InvalidOperationException("Invalid condition");
    }
}