using System.Collections;
using System.Globalization;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Expressions;

public record Expression
{
    /// <summary>Checks executable expressions without needing instance data.</summary>
    public void Validate()
    {
        switch (this)
        {
            case Call(Identifier(var name), var arguments):
                if (!Functions.TryGetValue(name, out var function))
                    throw new ArgumentException($"Unknown function '{name}'");
                if (arguments.Length != function.GetType().GetGenericArguments().Length - 1)
                    throw new ArgumentException($"Invalid number of arguments for '{name}'");
                foreach (var argument in arguments) argument.Validate();
                break;
            case Operator(OperatorType.Equal, var left, var right):
                left.Validate();
                right.Validate();
                break;
            case Index(var expression, Int(var index)) when index >= 0:
                expression.Validate();
                break;
            case Boolean or Int or Double or Text or Identifier or Template:
                break;
            default:
                throw new ArgumentException("Unsupported expression");
        }
    }

    public virtual object? Execute(ObjectContext context)
    {
        return this switch
        {
            Boolean(var b) => b,
            Int(var n) => n,
            Double(var d) => d,
            Text(var s) => s,
            Identifier(var k) => context.Get(k),
            Index(var exp, Int(var n)) => (exp.Execute(context) as IList)?[n],
            Call(Identifier("select"), [var collection, var field]) =>
                CallFunction(Functions["select"],
                    context.ResolveReferenceItems(collection.Execute(context), collection.Properties),
                    field.Execute(context)),
            Call(Identifier exp, var args) when Functions.ContainsKey(exp.Text) =>
                CallFunction(Functions[exp.Text], args.Select(a => a.Execute(context)).ToArray()),
            Call(Identifier(var text), var args) => context.Get(new ComplexLookup(text, args)),
            Operator(var type, var left, var right) => type switch
            {
                OperatorType.Equal => Equals(left.Execute(context), right.Execute(context)),
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };
    }

    private static object? CallFunction(Function function, params object?[] args)
    {
        var types = function.GetType().GetGenericArguments();
        // TODO: this might need more structure, but just handling some basic cases for now
        var convertedArguments = args.Select((arg, i) =>
        {
            var target = types[i];
            if (target == typeof(bool) && arg is not bool)
                return arg != null;
            if (target == typeof(string))
                return arg?.ToString();
            if ((target == typeof(DateTime) || target == typeof(DateTime?)) && arg is string s)
                return DateTime.TryParse(s, out var val) ? val : null;
            return arg;
        }).ToArray();
        return function.Call(convertedArguments);
    }

    private static readonly Dictionary<string, Function> Functions = new()
    {
        ["addDays"] = new Function<DateTime?, int, DateTime?>((d, i) => d?.AddDays(i)),
        ["addMonths"] = new Function<DateTime?, int, DateTime?>((d, i) => d?.AddMonths(i)),
        ["addWeeks"] = new Function<DateTime?, int, DateTime?>((d, i) => d?.AddDays(7 * i)),
        ["daysUntil"] = new Function<DateTime?, DateTime?, int?>((from, to) =>
            from == null || to == null ? null : (int)Math.Ceiling((to.Value.Date - from.Value.Date).TotalDays)),
        ["formatDate"] = new Function<DateTime?, string, string?>((d, f)
            => d?.ToString(f, CultureInfo.InvariantCulture)),
        ["dateLong"] = new Function<DateTime?, string?>(d => d?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)),
        ["dateShort"] = new Function<DateTime?, string?>(d => d?.ToString("dd/MM", CultureInfo.InvariantCulture)),
        ["if"] = new Function<bool, object?, object?, object?>((b, t1, t2) => b ? t1 : t2),
        ["contains"] = new Function<IEnumerable<object>, object, bool>((a, o) => a?.Contains(o) == true),
        ["count"] = new Function<object, int>(value => value switch
        {
            null => 0,
            IEnumerable values when values is not string => values.Cast<object?>().Count(),
            _ => throw new ArgumentException("count expects a collection")
        }),
        ["except"] = new Function<IEnumerable, object, object?[]>((list, excluded) =>
        {
            HashSet<object?> remove = excluded is IEnumerable values && excluded is not string
                ? values.Cast<object?>().ToHashSet()
                : [excluded];
            return list?.Cast<object?>().Where(item => !remove.Contains(item)).ToArray() ?? [];
        }),
        ["select"] = new Function<IEnumerable, string, object?[]>((list, field) =>
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("select expects a field name, e.g. select(List, =Field)");
            return list?.Cast<object?>()
                .Select(row => row switch
                {
                    Dictionary<string, object> dict => dict.GetValueOrDefault(field),
                    Dictionary<Lookup, object> dict => dict.GetValueOrDefault(field),
                    _ => null
                })
                .Where(value => !DataExtensions.IsEffectivelyEmpty(value))
                .ToArray() ?? [];
        }),
        ["flatten"] = new Function<IEnumerable, object?[]>(list =>
            list?.Cast<object?>()
                .SelectMany(item => item is IEnumerable values && item is not string
                    ? values.Cast<object?>()
                    : [item])
                .ToArray() ?? []),
        ["unique"] = new Function<IEnumerable, object?[]>(list =>
            list?.Cast<object?>()
                .DistinctBy(item => item is string s ? s.ToUpperInvariant() : item)
                .ToArray() ?? []),
        ["min"] = new Function<IEnumerable, object?>(list => FindExtreme(list, "min", -1)),
        ["max"] = new Function<IEnumerable, object?>(list => FindExtreme(list, "max", 1)),
        ["and"] = new Function<bool, bool, bool>((a, b) => a && b),
        ["or"] = new Function<bool, bool, bool>((a, b) => a || b),
        ["coalesce"] = new Function<object, object, object?>((value, fallback) => value ?? fallback),
    };

    /// <summary>
    /// Returns the smallest (direction -1) or largest (direction 1) number in the list, or null if it is empty.
    /// Compares as double when any value is floating point, as decimal otherwise (same as Value conditions).
    /// </summary>
    private static object? FindExtreme(IEnumerable? list, string name, int direction)
    {
        var values = list?.Cast<object?>().ToArray() ?? [];
        if (values.Any(v => v is not (int or long or float or double or decimal)))
            throw new ArgumentException($"{name} expects a list of numbers");
        if (values.Length == 0)
            return null;

        var useDouble = values.Any(v => v is float or double);

        int Compare(object? a, object? b) => useDouble
            ? Convert.ToDouble(a).CompareTo(Convert.ToDouble(b))
            : Convert.ToDecimal(a).CompareTo(Convert.ToDecimal(b));

        return values.Aggregate((best, value) => Compare(value, best) * direction > 0 ? value : best);
    }

    public virtual IEnumerable<Lookup> Properties => this switch
    {
        // Value sets come from the model, not from instance data, so there is nothing to load
        Identifier(var k) when k.StartsWith("ValueSets.", StringComparison.Ordinal) => [],
        Identifier(var k) => [k],
        Call(Identifier("select"), [var collection, Text(var selectedField)]) =>
            collection.Properties.Select(lookup => lookup is PropertyLookup property
                ? new PropertyLookup(property.Property + "." + selectedField)
                : lookup),
        Call(Identifier(var text), var args) when Functions.ContainsKey(text) => args.SelectMany(a => a.Properties),
        Call(Identifier(var text) expr, var args) => [new ComplexLookup(text, args)],
        Index(var exp, _) => exp.Properties,
        Operator(_, var left, var right) => [.. left.Properties, .. right.Properties],
        _ => []
    };
}

abstract class Function
{
    public abstract object? Call(object?[] args);
}

class Function<T, TOut>(Func<T?, TOut> func) : Function
{
    public override object? Call(object?[] args)
    {
        if (args.Length != 1)
            throw new Exception("Invalid number of arguments");
        return func((T?)args[0]);
    }
}

class Function<T1, T2, TOut>(Func<T1?, T2?, TOut> func) : Function
{
    public override object? Call(object?[] args)
    {
        if (args.Length != 2)
            throw new Exception("Invalid number of arguments");
        return func((T1?)args[0], (T2?)args[1]);
    }
}

class Function<T1, T2, T3, TOut>(Func<T1?, T2?, T3?, TOut> func) : Function
{
    public override object? Call(object?[] args)
    {
        if (args.Length != 3)
            throw new Exception("Invalid number of arguments");
        return func((T1?)args[0], (T2?)args[1], (T3?)args[2]);
    }
}

public enum OperatorType
{
    Equal,
    LessThanOrEqual,
    GreaterThanOrEqual
};

public record Call(Expression Function, params Expression[] Arguments) : Expression;

public record Operator(OperatorType Type, Expression Left, Expression Right) : Expression;

public record Identifier(string Text) : Expression;

public record Int(int Value) : Expression;

public record Double(double Value) : Expression;

public record Boolean(bool Value) : Expression;

public record Text(string Value) : Expression;

public record Index(Expression Expression, Expression Key) : Expression;