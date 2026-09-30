using UvA.Workflow.Events;
using UvA.Workflow.Journaling;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Deadlines;

/// <summary>
/// Reads deadline changes from the normal instance journal and form event log.
/// A separate snapshot store is unnecessary: the journal already records the value
/// before each update, while the event log identifies the form submission that made it.
/// </summary>
public static class DeadlineHistory
{
    public static PropertyDefinition? GetProperty(Step step, WorkflowDefinition definition)
        => definition.Properties.FirstOrDefault(p => p.Name == step.Deadline?.Date.Trim() && !p.IsArray &&
                                                     p.DataType is DataType.Date or DataType.DateTime);

    public static (DateTimeOffset? PreviousDate, BilingualString? Reason) GetChange(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        string property,
        WorkflowInstanceHistory history)
    {
        var changes = history.Journal?.PropertyChanges
            .Where(change => change.Path == property && change.OldValue is BsonDateTime)
            .OrderBy(change => change.Timestamp)
            .ToArray() ?? [];
        var change = changes.LastOrDefault();
        if (change?.OldValue is not BsonDateTime previous)
            return (null, null);

        var form = definition.Forms.FirstOrDefault(form =>
            form.Step == null && form.OnSubmit.Any(effect => effect.SetProperty?.Property == property));
        var submittedAt = form == null
            ? null
            : history.EventLogs
                .Where(log => log.EventId == form.Name &&
                              log.Operation is EventLogOperation.Create or EventLogOperation.Update)
                .Select(log => (DateTime?)(log.EventDate ?? log.Timestamp))
                .Where(date => date.HasValue && date.Value <= change.Timestamp)
                .Max();

        var reason = submittedAt == null
            ? null
            : ResolveReason(instance, definition, form!, history.Journal, submittedAt.Value);
        return (new DateTimeOffset(previous.ToUniversalTime()), reason);
    }

    public static DateTimeOffset? GetMaximumDate(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        string property,
        InstanceJournalEntry? journal)
    {
        var configuredSteps = definition.AllSteps
            .Where(step => GetProperty(step, definition)?.Name == property)
            .ToArray();
        var days = configuredSteps
            .Select(step => step.Deadline!.MaxPostponementDays)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Min();
        if (configuredSteps.All(step => step.Deadline!.MaxPostponementDays == null))
            return null;

        var original = journal?.PropertyChanges
                           .Where(change => change.Path == property && change.OldValue is BsonDateTime)
                           .OrderBy(change => change.Timestamp)
                           .Select(change => change.OldValue)
                           .FirstOrDefault()
                       ?? instance.GetProperty(property);
        if (original is not BsonDateTime date)
            return null;

        // Extend calendar days in server time so daylight saving keeps the deadline's clock time.
        try
        {
            return new DateTimeOffset(date.ToLocalTime().AddDays(days));
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.MaxValue;
        }
    }

    // Validate every configured date update before running any effects.
    public static IEnumerable<InvalidQuestion> ValidateUpdates(
        WorkflowInstance instance,
        Form form,
        ObjectContext context,
        InstanceJournalEntry? journal)
    {
        var definition = form.WorkflowDefinition;
        foreach (var effect in form.OnSubmit.Where(effect =>
                     effect.SetProperty != null && effect.Condition.IsMet(context)))
        {
            var update = effect.SetProperty!;
            if (!definition.AllSteps.Any(step => GetProperty(step, definition)?.Name == update.Property))
                continue;

            var value = ToDateTimeOffset(update.ValueExpression.Execute(context));
            var maximum = GetMaximumDate(instance, definition, update.Property, journal);
            var error = value == null
                ? "InvalidDeadline"
                : maximum != null && value > maximum
                    ? "MaximumExtensionExceeded"
                    : null;
            if (error == null)
                continue;

            var question = form.PropertyDefinitions.FirstOrDefault(question =>
                question.Condition.IsMet(context) &&
                update.ValueExpression.Properties.Any(property => property.ToString() == question.Name));
            yield return new InvalidQuestion(question?.Name ?? update.Property, error);
        }
    }

    private static BilingualString? ResolveReason(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        Form form,
        InstanceJournalEntry? journal,
        DateTime submittedAt)
    {
        var reasonValue = ValueAt(instance, journal, form, "PostponementReason", submittedAt);
        var reason = reasonValue is BsonString textValue ? textValue.Value : reasonValue?.ToString();
        var reasonText = definition.Properties.FirstOrDefault(property => property.Name == "PostponementReason")
            ?.Values?.FirstOrDefault(value => value.Name == reason)?.Text;
        var explanation = ValueAt(instance, journal, form, "PostponementExplanation", submittedAt);
        if (explanation is not BsonString { Value.Length: > 0 } text)
            return reasonText;

        return reasonText == null ? (BilingualString)text.Value : reasonText + (BilingualString)$": {text.Value}";
    }

    private static BsonValue? ValueAt(
        WorkflowInstance instance,
        InstanceJournalEntry? journal,
        Form form,
        string property,
        DateTime submittedAt)
    {
        var paths = form.PropertyName == null
            ? new[] { property }
            : new[] { property, $"{form.PropertyName}.{property}" };
        var value = journal?.PropertyChanges
            .Where(change => paths.Contains(change.Path) && change.Timestamp >= submittedAt)
            .OrderBy(change => change.Timestamp)
            .Select(change => change.OldValue)
            .FirstOrDefault();
        return value ?? instance.GetProperty(form.PropertyName, property);
    }

    private static DateTimeOffset? ToDateTimeOffset(object? value)
        => value switch
        {
            DateTimeOffset date => date,
            DateTime date => new DateTimeOffset(date),
            _ => null
        };
}