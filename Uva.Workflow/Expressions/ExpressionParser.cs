using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Uva.Workflow.Expressions;

public class ExpressionParser
{
    Expression Parse(Queue<string> tokens)
    {
        var current = ParseSingle(tokens.Dequeue());
        while (tokens.Any())
        {
            if (tokens.Peek() is ")" or "]")
                return current;
            var next = tokens.Dequeue();
            current = next switch
            {
                "(" when current is Identifier("template") => new Template(ParseTemplate(tokens)),
                "(" => new Call(current, ParseArguments(tokens)),
                "[" => new Index(current, ParseArguments(tokens).First()),
                "==" => new Operator(OperatorType.Equal, current, ParseSingle(tokens.Dequeue())),
                "<=" => new Operator(OperatorType.LessThanOrEqual, current, ParseSingle(tokens.Dequeue())),
                ">=" => new Operator(OperatorType.GreaterThanOrEqual, current, ParseSingle(tokens.Dequeue())),
                "," => current,
                _ => throw new Exception("Unexpected token")
            };
            if (next == ",")
                return current;
        }

        return current;
    }

    string ParseTemplate(Queue<string> tokens)
    {
        var content = tokens.Dequeue().TrimStart('"').TrimEnd('"');
        tokens.Dequeue();
        return content;
    }

    private Expression[] ParseArguments(Queue<string> tokens)
    {
        var args = new List<Expression>();
        while (tokens.Peek() != ")" && tokens.Peek() != "]")
            args.Add(Parse(tokens));
        tokens.Dequeue();
        return args.ToArray();
    }

    private Expression ParseSingle(string token)
    {
        if (token.StartsWith("="))
            return new Text(token.Substring(1));
        if (bool.TryParse(token, out var b))
            return new Boolean(b);
        if (int.TryParse(token, out var x))
            return new Number(x);
        return new Identifier(token);
    }

    private List<string> Tokenize(string exp)
    {
        var chars = new List<char> { ',', '(', ')', '[', ']' };
        var operatorChars = new List<char> { '<', '>', '=' };
        var tokens = new List<string>();
        int start = 0;
        for (var i = 0; i < exp.Length; i++)
        {
            var isOperator = operatorChars.Contains(exp[i]) && operatorChars.Contains(exp[i + 1]);
            if (chars.Contains(exp[i]) || isOperator)
            {
                if (i - start > 0)
                    tokens.Add(exp.Substring(start, i - start).Trim());
                var length = isOperator ? 2 : 1;
                tokens.Add(exp[i..(i + length)]);
                start = i + length;
                i += length - 1;
            }
        }

        if (start < exp.Length)
            tokens.Add(exp.Substring(start));

        return tokens;
    }

    private static readonly ConcurrentDictionary<string, Expression> Cache = new();

    [return: NotNullIfNotNull(nameof(exp))]
    public static Expression? Parse(string? exp)
    {
        if (exp == null)
            return null;
        return Cache.GetOrAdd(exp, s =>
        {
            var parser = new ExpressionParser();
            var tokens = parser.Tokenize(s);
            return parser.Parse(new Queue<string>(tokens));
        });
    }
}