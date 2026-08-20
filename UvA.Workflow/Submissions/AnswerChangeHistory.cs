using UvA.Workflow.Journaling;

namespace UvA.Workflow.Submissions;

public record AnswerChange(int Version, BsonValue? Value, DateTime ChangedAt, string? ChangedBy);

public static class AnswerChangeHistory
{
    public static AnswerChange[] For(
        IEnumerable<PropertyChangeEntry> journal,
        string questionName,
        string? formPropertyName,
        DateTime? dateSubmitted,
        BsonValue? currentValue)
    {
        if (dateSubmitted == null)
            return [];

        var nestedPath = formPropertyName == null ? null : $"{formPropertyName}.{questionName}";
        var edits = journal
            .Where(change => change.Path == questionName || change.Path == nestedPath)
            .Where(change => change.Timestamp > dateSubmitted)
            .OrderBy(change => change.Timestamp)
            .ToArray();

        if (edits.Length == 0)
            return [];

        // Journal stores the value before each edit, so values[0] is the submitted
        // answer and values[i + 1] is the answer after edits[i] (last = current).
        var values = edits.Select(edit => edit.OldValue).Append(currentValue).ToArray();
        var oldestFirst = edits.Select((edit, i) =>
            new AnswerChange(i + 2, values[i + 1], edit.Timestamp, edit.ModifiedBy));
        return oldestFirst
            .Prepend(new AnswerChange(1, values[0], dateSubmitted.Value, null))
            .Reverse()
            .ToArray();
    }
}