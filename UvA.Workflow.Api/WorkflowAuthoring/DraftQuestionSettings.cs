using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace UvA.Workflow.Api.WorkflowAuthoring;

internal static partial class DraftQuestionSettings
{
    public static string? Read(YamlMappingNode node, string name)
    {
        if (!DraftYaml.IsUnmarked(node) || Text(node, "name") != name || !Identifier().IsMatch(name) ||
            Text(node, "type") is not { } rawType)
            return null;

        var isArray = rawType.StartsWith('[');
        var type = rawType.TrimEnd('!').Trim('[', ']');
        if (rawType != (isArray ? $"[{type}]" : type) + (rawType.EndsWith('!') ? "!" : ""))
            return null;

        var control = Text(node, "control");
        var supportsType = type switch
        {
            "String" => !isArray && control is null or "TextArea" or "Email" or "Phone",
            "Double" or "Int" or "Date" or "Check" => !isArray && control is null,
            "File" or "User" => control is null,
            _ => Identifier().IsMatch(type) && control is null or "RadioList" or "Dropdown"
        };
        if (!supportsType)
            return null;

        try
        {
            return JsonSerializer.Serialize(node.Children.ToDictionary(
                entry => ((YamlScalarNode)entry.Key).Value!, entry => ReadSetting(entry.Key, entry.Value, type)));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static readonly ISerializer BlockYaml = new SerializerBuilder()
        .WithQuotingNecessaryStrings(true).WithIndentedSequences().DisableAliases().Build();

    private static readonly char[] LineBreaks = ['\r', '\n'];

    public static string Apply(string source, YamlNode question, string before, string after)
    {
        using var original = JsonDocument.Parse(before);
        using var revised = JsonDocument.Parse(after);
        var edits = new List<(int Start, int End, string Text)>();
        var lineBreak = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        Patch(question, original.RootElement, revised.RootElement, inFlow: false);
        // Apply outer insertions first when a child and its parent end on the same line.
        foreach (var edit in edits.AsEnumerable().Reverse().OrderByDescending(edit => edit.Start))
            source = source[..edit.Start] + edit.Text + source[edit.End..];
        return source;

        void Patch(YamlNode node, JsonElement oldValue, JsonElement newValue, bool inFlow)
        {
            if (JsonElement.DeepEquals(oldValue, newValue))
                return;

            if (node is YamlMappingNode map && newValue.ValueKind == JsonValueKind.Object)
            {
                var removed = oldValue.EnumerateObject().Where(field => !newValue.TryGetProperty(field.Name, out _));
                var added = newValue.EnumerateObject().Where(field => !oldValue.TryGetProperty(field.Name, out _));
                var retained = map.Children
                    .Where(entry => newValue.TryGetProperty(((YamlScalarNode)entry.Key).Value!, out _))
                    .ToArray();
                var structureChanged = removed.Any() || added.Any();
                if (!structureChanged || (map.Style == MappingStyle.Block && retained.Length > 0))
                {
                    foreach (var (key, value) in retained)
                    {
                        var name = ((YamlScalarNode)key).Value!;
                        Patch(value, oldValue.GetProperty(name), newValue.GetProperty(name),
                            map.Style == MappingStyle.Flow);
                    }

                    foreach (var field in removed)
                    {
                        var key = map.Children.Keys.OfType<YamlScalarNode>().Single(key => key.Value == field.Name);
                        Remove(Start(key), map.Children[key]);
                    }

                    if (added.ToArray() is { Length: > 0 } fields)
                        Append(retained[^1].Value, Indent(map),
                            fields.ToDictionary(field => field.Name, field => ToYamlValue(field.Value)));
                    return;
                }
            }

            if (node is YamlSequenceNode sequence && newValue.ValueKind == JsonValueKind.Array &&
                TryPatchSequence(sequence, oldValue, newValue))
                return;

            // shortcut: re-render only the affected collection for structural changes to flow YAML or reordered items.
            var block = node is YamlMappingNode { Style: MappingStyle.Block } or
                YamlSequenceNode { Style: SequenceStyle.Block };
            var replacement = block && newValue.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? RenderBlock(ToYamlValue(newValue), Indent(node), lineBreak)
                : RenderInline(newValue, (node as YamlScalarNode)?.Style ?? ScalarStyle.Any, inFlow);
            edits.Add((Start(node), ContentEnd(source, node), replacement));
        }

        bool TryPatchSequence(YamlSequenceNode sequence, JsonElement oldValue, JsonElement newValue)
        {
            var previous = oldValue.EnumerateArray().ToArray();
            var next = newValue.EnumerateArray().ToArray();
            var oldKeys = previous.Select(ItemKey).ToArray();
            var newKeys = next.Select(ItemKey).ToArray();
            var newKeySet = newKeys.ToHashSet(StringComparer.Ordinal);
            var retained = oldKeys.Select((key, index) => (key, index))
                .Where(item => newKeySet.Contains(item.key)).ToArray();
            var stableOrder = retained.Select(item => item.key).SequenceEqual(newKeys.Take(retained.Length));
            if (!stableOrder || retained.Length == 0 || oldKeys.Distinct().Count() != oldKeys.Length ||
                newKeySet.Count != newKeys.Length)
                return false;

            var removed = Enumerable.Range(0, previous.Length).Except(retained.Select(item => item.index))
                .Select(index => sequence.Children[index]).ToArray();
            var structureChanged = removed.Length > 0 || retained.Length != next.Length;
            if (structureChanged && (sequence.Style != SequenceStyle.Block ||
                                     removed.Any(child => !HasInlineDash(source, child))))
                return false;

            for (var index = 0; index < retained.Length; index++)
            {
                var originalIndex = retained[index].index;
                Patch(sequence.Children[originalIndex], previous[originalIndex], next[index],
                    sequence.Style == SequenceStyle.Flow);
            }

            foreach (var child in removed)
                Remove(LineStart(source, Start(child)), child);
            if (retained.Length < next.Length)
                Append(sequence.Children[retained[^1].index], Indent(sequence),
                    next.Skip(retained.Length).Select(ToYamlValue).ToArray());
            return true;
        }

        void Remove(int start, YamlNode value)
        {
            var lineStart = LineStart(source, start);
            var lineEnd = LineEnd(source, ContentEnd(source, value));
            if (string.IsNullOrWhiteSpace(source[lineStart..start]))
            {
                var end = lineEnd;
                if (end < source.Length && source[end] == '\r') end++;
                if (end < source.Length && source[end] == '\n') end++;
                edits.Add((lineStart, end, ""));
            }
            else
                // The first key can be on the sequence item's dash line.
                edits.Add((lineStart + source[lineStart..start].TrimEnd().Length, lineEnd, ""));
        }

        void Append(YamlNode previous, int column, object value)
        {
            var index = LineEnd(source, ContentEnd(source, previous));
            var indentation = new string(' ', column);
            edits.Add((index, index, lineBreak + indentation + RenderBlock(value, column, lineBreak)));
        }
    }

    // The last line's inline comment belongs to the edited block, even when a later save appends a key.
    public static int ScopeEnd(string source, YamlNode node)
        => node is YamlMappingNode { Style: MappingStyle.Block }
            ? LineEnd(source, ContentEnd(source, node))
            : End(source, node);

    private static string ItemKey(JsonElement item)
        => item.ValueKind == JsonValueKind.Object ? item.GetProperty("name").GetString()! : item.GetString()!;

    private static bool HasInlineDash(string source, YamlNode node)
        => source[LineStart(source, Start(node))..Start(node)].Trim() == "-";

    private static int Start(YamlNode node) => checked((int)node.Start.Index);

    private static int Indent(YamlNode node) => checked((int)node.Start.Column - 1);

    private static int ContentEnd(string source, YamlNode node)
    {
        var end = End(source, node);
        while (end > Start(node) && source[end - 1] is '\r' or '\n')
            end--;
        return end;
    }

    private static int LineStart(string source, int index)
        => index == 0 ? 0 : source.LastIndexOfAny(LineBreaks, index - 1) + 1;

    private static int LineEnd(string source, int index)
        => source.IndexOfAny(LineBreaks, index) is var end && end >= 0 ? end : source.Length;

    private static object ToYamlValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject()
            .ToDictionary(field => field.Name, field => ToYamlValue(field.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(ToYamlValue).ToArray(),
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.GetInt32(),
        _ => value.GetBoolean()
    };

    private static string RenderBlock(object value, int column, string lineBreak)
        => BlockYaml.Serialize(value).TrimEnd(LineBreaks).Replace("\r\n", "\n")
            .Replace("\n", lineBreak + new string(' ', column));

    private static string RenderInline(JsonElement value, ScalarStyle style = ScalarStyle.Any, bool inFlow = true)
        => value.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(", ", value.EnumerateObject().Select(field =>
                field.Name + ": " + RenderInline(field.Value))) + "}",
            JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(item => RenderInline(item))) +
                                   "]",
            JsonValueKind.String => RenderString(value.GetString()!, style, inFlow),
            _ => value.GetRawText()
        };

    internal static string RenderString(string value, ScalarStyle style, bool inFlow)
    {
        var singleLine = !value.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029');
        if (style == ScalarStyle.SingleQuoted && singleLine)
            return "'" + value.Replace("'", "''") + "'";
        if (value.Length == 0 || style == ScalarStyle.DoubleQuoted || !singleLine ||
            (inFlow && value.IndexOfAny([',', '[', ']', '{', '}', ':']) >= 0))
            return DraftYaml.Quote(value);
        var rendered = BlockYaml.Serialize(value).TrimEnd(LineBreaks);
        return rendered.IndexOfAny(LineBreaks) >= 0 || rendered.StartsWith("--- ", StringComparison.Ordinal)
            ? DraftYaml.Quote(value)
            : rendered;
    }

    // Block collections end at the next token, so use the last child instead of consuming that indentation.
    private static int End(string source, YamlNode node) => node switch
    {
        YamlMappingNode { Style: MappingStyle.Block } map => map.Children.Values.Max(child => End(source, child)),
        YamlSequenceNode { Style: SequenceStyle.Block } list => list.Children.Max(child => End(source, child)),
        YamlMappingNode or YamlSequenceNode => FlowEnd(source, checked((int)node.Start.Index)),
        _ => checked((int)node.End.Index)
    };

    private static int FlowEnd(string source, int start)
    {
        using var reader = new StringReader(source[start..]);
        var parser = new Parser(reader);
        var depth = 0;
        while (parser.MoveNext())
        {
            if (parser.Current is MappingStart or SequenceStart)
                depth++;
            if (parser.Current is MappingEnd or SequenceEnd && --depth == 0)
                return start + checked((int)parser.Current.Start.Index) + 1;
        }

        throw Invalid();
    }

    private static object ReadSetting(YamlNode key, YamlNode value, string type) => ((YamlScalarNode)key).Value switch
    {
        "name" or "type" or "control" => Scalar(value),
        "text" or "description" => Localized(value),
        "allowsExternalUsers" when type == "User" && bool.TryParse(Scalar(value), out var flag) => flag,
        "maximumSize" when type == "File" && int.TryParse(Scalar(value), out var size) && size > 0 => size,
        "allowedTypes" when type == "File" && value is YamlSequenceNode types =>
            types.Children.Select(Scalar).ToArray(),
        "values" when type is not ("String" or "Double" or "Int" or "Date" or "Check" or "User" or "File") &&
                      value is YamlSequenceNode values => values.Children.Select(Choice).ToArray(),
        _ => throw Invalid()
    };

    private static Dictionary<string, object> Choice(YamlNode value)
    {
        if (value is not YamlMappingNode choice || choice.Children.Count != 2 ||
            Text(choice, "name") is not { } name || !Identifier().IsMatch(name) ||
            DraftYaml.Child(choice, "text") is not { } label)
            throw Invalid();
        return new() { ["name"] = name, ["text"] = Localized(label) };
    }

    private static object Localized(YamlNode value)
        => value is YamlMappingNode map
            ? map.Children.ToDictionary(entry => entry.Key is YamlScalarNode { Value: "en" or "nl" } key
                ? key.Value!
                : throw Invalid(), entry => PlainText(entry.Value))
            : PlainText(value);

    private static string PlainText(YamlNode value)
    {
        var text = Scalar(value);
        if (text.Length > 2000 || text.Any(c => c is '{' or '}' || char.IsControl(c) && c != '\n'))
            throw Invalid();
        return text;
    }

    private static string? Text(YamlMappingNode node, string key)
        => (DraftYaml.Child(node, key) as YamlScalarNode)?.Value;

    private static string Scalar(YamlNode node) => node is YamlScalarNode { Value: { } text } ? text : throw Invalid();

    private static ArgumentException Invalid() => new("One or more question settings are unsupported.");

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_-]{0,127}\z")]
    private static partial Regex Identifier();
}