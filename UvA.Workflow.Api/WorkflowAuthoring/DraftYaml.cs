using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace UvA.Workflow.Api.WorkflowAuthoring;

internal static class DraftYaml
{
    public static YamlMappingNode ParseRoot(string source)
    {
        var yaml = new YamlStream();
        using var reader = new StringReader(source);
        yaml.Load(reader);

        return yaml.Documents.Count == 1 && yaml.Documents[0].RootNode is YamlMappingNode root
            ? root
            : throw new InvalidOperationException("Expected one YAML mapping.");
    }

    public static YamlNode? Child(YamlMappingNode? mapping, string key)
        => mapping is not null && mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    public static string? Name(YamlMappingNode node) => (Child(node, "name") as YamlScalarNode)?.Value;

    // Tagged or aliased values can change meaning beyond the edited field.
    public static bool IsUnmarked(YamlNode node)
        => node.Anchor.IsEmpty && node.Tag.IsEmpty && (node switch
        {
            YamlMappingNode map => map.Children.All(entry => entry.Key is YamlScalarNode &&
                                                             IsUnmarked(entry.Key) && IsUnmarked(entry.Value)),
            YamlSequenceNode list => list.Children.All(IsUnmarked),
            _ => node is YamlScalarNode
        });

    public static string Quote(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")
            .Replace("\r", "\\r").Replace("\t", "\\t").Replace("\u2028", "\\L").Replace("\u2029", "\\P");
        return $"\"{escaped}\"";
    }

    public static string PreserveFormatting(string original, string proposed, YamlMappingNode before,
        YamlMappingNode after)
    {
        if (before.Equals(after))
            return original;

        var edits = new List<(int Offset, Func<string, string> Apply)>();
        Collect(before, after, false);
        var patched = original;
        foreach (var edit in edits.OrderByDescending(edit => edit.Offset))
            patched = edit.Apply(patched);

        try
        {
            return ParseRoot(patched).Equals(after) ? patched : proposed;
        }
        catch (YamlException)
        {
            return proposed;
        }

        void Collect(YamlNode previous, YamlNode next, bool inFlow)
        {
            if (previous.Equals(next))
                return;
            if (previous is YamlScalarNode from && next is YamlScalarNode { Value: { } value } &&
                from.Style is ScalarStyle.Plain or ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted &&
                from.Start.Line == from.End.Line)
            {
                var edit = new DraftTextEdit(checked((int)from.Start.Index), checked((int)from.End.Index),
                    DraftQuestionSettings.RenderString(value, from.Style, inFlow));
                edits.Add((edit.Start, edit.Apply));
            }
            else if (previous is YamlScalarNode { Style: ScalarStyle.Literal or ScalarStyle.Folded } &&
                     next is YamlScalarNode { Style: ScalarStyle.Literal or ScalarStyle.Folded } &&
                     previous.Start.Column == next.Start.Column)
            {
                var replacement = proposed[checked((int)next.Start.Index)..checked((int)next.End.Index)];
                var lineBreak = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var edit = new DraftTextEdit(checked((int)previous.Start.Index), checked((int)previous.End.Index),
                    replacement.ReplaceLineEndings(lineBreak));
                edits.Add((edit.Start, edit.Apply));
            }
            else if (previous is YamlMappingNode left && next is YamlMappingNode right)
            {
                if (Name(left) is { } name && DraftQuestionSettings.Read(left, name) is { } beforeSettings &&
                    DraftQuestionSettings.Read(right, name) is { } afterSettings)
                {
                    edits.Add((checked((int)left.Start.Index),
                        source => DraftQuestionSettings.Apply(source, left, beforeSettings, afterSettings)));
                    return;
                }

                foreach (var (key, node) in left.Children)
                    if (right.Children.TryGetValue(key, out var proposedChild))
                    {
                        if (key is YamlScalarNode { Value: "elements" } &&
                            node is YamlSequenceNode previousElements &&
                            proposedChild is YamlSequenceNode nextElements &&
                            CollectElementMoves(previousElements, nextElements))
                            continue;
                        Collect(node, proposedChild, left.Style == MappingStyle.Flow);
                    }
            }
            else if (previous is YamlSequenceNode oldItems && next is YamlSequenceNode newItems &&
                     oldItems.Children.Count == newItems.Children.Count)
            {
                foreach (var (first, second) in oldItems.Children.Zip(newItems.Children))
                    Collect(first, second, oldItems.Style == SequenceStyle.Flow);
            }
        }

        bool CollectElementMoves(YamlSequenceNode previous, YamlSequenceNode next)
        {
            if (previous.Children.Count != next.Children.Count || previous.Equals(next))
                return false;

            var indices = Enumerable.Range(0, previous.Children.Count).ToArray();
            var sameSlot = indices.Where(index => previous.Children[index].Equals(next.Children[index])).ToHashSet();
            var available = indices.Where(index => !sameSlot.Contains(index))
                .GroupBy(index => ElementKey(previous.Children[index]))
                .ToDictionary(group => group.Key, group => new Queue<int>(group));
            var order = indices.Select(index =>
            {
                if (sameSlot.Contains(index))
                    return index;

                return available.TryGetValue(ElementKey(next.Children[index]), out var sources) &&
                       sources.TryDequeue(out var source) && previous.Children[source].Equals(next.Children[index])
                    ? source
                    : -1;
            }).ToArray();

            var moves = indices.Where(index => order[index] >= 0 && order[index] != index).ToArray();
            if (moves.Length == 0)
                return false;

            // An edited element with no match can stay in its slot if nothing else moved from there.
            var used = order.Where(index => index >= 0).ToHashSet();
            if (indices.Any(index => order[index] < 0 && used.Contains(index)))
                return true;

            var block = previous.Style == SequenceStyle.Block;
            var movedSlots = moves.ToDictionary(index => index, index =>
            {
                var node = previous.Children[index];
                var end = DraftQuestionSettings.ScopeEnd(original, node);
                if (block)
                {
                    var lineEnd = original.AsSpan(end).IndexOfAny('\r', '\n');
                    end = lineEnd < 0 ? original.Length : end + lineEnd;
                }

                return (Start: checked((int)node.Start.Index), End: end, node.Start.Column);
            });
            if (moves.Any(index => !movedSlots.TryGetValue(order[index], out var source) ||
                                   source.Column != movedSlots[index].Column ||
                                   index + 1 < indices.Length &&
                                   movedSlots[index].End > previous.Children[index + 1].Start.Index))
                return true;

            foreach (var index in moves)
            {
                var slot = movedSlots[index];
                var source = movedSlots[order[index]];
                var edit = new DraftTextEdit(slot.Start, slot.End, original[source.Start..source.End]);
                edits.Add((edit.Start, edit.Apply));
            }

            foreach (var index in indices.Where(index => order[index] < 0))
                Collect(previous.Children[index], next.Children[index], previous.Style == SequenceStyle.Flow);
            return true;
        }
    }

    private static (string? Question, string? Text, string? English, string? Dutch, YamlNode? Callout)
        ElementKey(YamlNode node)
    {
        var element = node as YamlMappingNode;
        var text = Child(element, "text");
        var translations = text as YamlMappingNode;
        return ((Child(element, "question") as YamlScalarNode)?.Value, (text as YamlScalarNode)?.Value,
            (Child(translations, "en") as YamlScalarNode)?.Value, (Child(translations, "nl") as YamlScalarNode)?.Value,
            Child(element, "callout"));
    }
}

internal readonly record struct DraftTextEdit(int Start, int End, string Value)
{
    public string Apply(string source) => source[..Start] + Value + source[End..];
}