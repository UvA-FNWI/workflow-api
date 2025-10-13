using System.Text;
using System.Text.RegularExpressions;

namespace UvA.Workflow.Expressions;

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

    public string Apply(ObjectContext context)
    {
        var builder = new StringBuilder();
        foreach (var part in _parts)
            builder.Append(part switch
            {
                Text t => t.Content,
                Value v => v.Content.Execute(context)?.ToString(),
                _ => null
            });
        return builder.ToString();
    }

    [GeneratedRegex("{{[^}]+}}")]
    private static partial Regex TemplateExpression();
}