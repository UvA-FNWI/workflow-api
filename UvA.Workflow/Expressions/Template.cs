using System.Collections;
using System.Text;
using System.Text.RegularExpressions;

namespace UvA.Workflow.Expressions;

public record BilingualTemplate(Template En, Template Nl)
{
    public static BilingualTemplate? Create(BilingualString? source)
        => source == null ? null : new BilingualTemplate(new Template(source.En), new Template(source.Nl));

    public BilingualString Apply(ObjectContext context)
        => new(En.Apply(context), Nl.Apply(context));
}

public partial record Template : Expression
{
    private record Part();

    private record Value(Expression Content) : Part;

    private record Text(string Content) : Part;

    public override Lookup[] Properties { get; }
    private readonly List<Part> _parts = [];

    public static Template? Create(string? source)
        => source == null ? null : new(source);

    public Template(string template)
    {
        var matches = TemplateExpression().Matches(template);
        Properties = matches.Select(m => (Lookup)m.Value[2..^2].Trim()).ToArray();
        var i = 0;
        foreach (Match match in matches)
        {
            if (match.Index != i)
                _parts.Add(new Text(template[i..match.Index]));
            _parts.Add(new Value(ExpressionParser.Parse(match.Value[2..^2].Trim())));
            i = match.Index + match.Length;
        }

        _parts.Add(new Text(template[i..]));
        Properties = _parts.Where(p => p is Value).Cast<Value>().SelectMany(v => v.Content.Properties).ToArray();
    }

    public override string Execute(ObjectContext context)
        => Apply(context);

    private static string? Render(Value v, ObjectContext context)
    {
        var result = v.Content.Execute(context);
        if (result is IEnumerable list && list is not string)
            return list.Cast<object>().ToSeparatedString();
        return result?.ToString();
    }

    public string Apply(ObjectContext context)
    {
        var builder = new StringBuilder();
        foreach (var part in _parts)
            builder.Append(part switch
            {
                Text t => t.Content,
                Value v => Render(v, context),
                _ => null
            });
        return builder.ToString();
    }

    [GeneratedRegex("{{[^}]+}}")]
    private static partial Regex TemplateExpression();
}