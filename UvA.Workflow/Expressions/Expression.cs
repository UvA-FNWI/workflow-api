using System.Collections;

namespace UvA.Workflow.Expressions;

public record Expression
{
    public virtual object? Execute(ObjectContext context)
    {
        return this switch
        {
            Boolean(var b) => b,
            Number(var n) => n,
            Text(var s) => s,
            Identifier(var k) => context.Get(k),
            Index(var exp, Number(var n)) => (exp.Execute(context) as IList)?[n],
            Call(Identifier exp, var args) when Functions.ContainsKey(exp.Text) => Functions[exp.Text]
                .Call(args.Select(a => a.Execute(context)).ToArray()),
            Call(Identifier(var text), var args) => context.Get(new ComplexLookup(text, args)),
            _ => throw new NotImplementedException()
        };
    }

    private static readonly Dictionary<string, Function> Functions = new()
    {
        ["addDays"] = new Function<DateTime?, int, DateTime?>((d, i) => d?.AddDays(i)),
        ["if"] = new Function<bool, object?, object?, object?>((b, t1, t2) => b ? t1 : t2),
        ["contains"] = new Function<IEnumerable<object>, object, bool>((a, o) => a?.Contains(o) == true),
        ["and"] = new Function<bool, bool, bool>((a, b) => a && b),
    };

    public virtual IEnumerable<Lookup> Properties => this switch
    {
        Identifier(var k) => [k],
        Call(Identifier(var text), var args) when Functions.ContainsKey(text) => args.SelectMany(a => a.Properties),
        Call(Identifier(var text) expr, var args) => [new ComplexLookup(text, args)],
        Index(var exp, _) => exp.Properties,
        _ => []
    };
}

abstract class Function
{
    public abstract object? Call(object?[] args);
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

public record Number(int Value) : Expression;

public record Boolean(bool Value) : Expression;

public record Text(string Value) : Expression;

public record Index(Expression Expression, Expression Key) : Expression;