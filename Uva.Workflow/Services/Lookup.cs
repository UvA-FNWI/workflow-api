using System.Diagnostics.CodeAnalysis;

namespace Uva.Workflow.Services;

public abstract record Lookup
{
    [return: NotNullIfNotNull(nameof(s))]
    public static implicit operator Lookup?(string? s) => s == null ? null : new PropertyLookup(s);
}

public record PropertyLookup(string Property) : Lookup
{
    public string[] Parts => Property.Split('.');
    public override string ToString() => Property;
}

public record ComplexLookup(string Function, params Expressions.Expression[] Arguments) : Lookup;