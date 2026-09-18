using UvA.Workflow.Jobs;
using UvA.Workflow.Journaling;
using UvA.Workflow.WorkflowModel;
using Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Deadlines;

public class PostponeDeadlineService(
    ModelService modelService,
    InstanceService instanceService,
    IInstanceJournalService journalService,
    JobService jobService)
{
    // Direct date properties opt in; literal dates and calculated expressions remain fixed.
    public static PropertyDefinition? GetDeadlineProperty(Step step, WorkflowDefinition definition)
        => step.Deadline == null
            ? null
            : definition.Properties.FirstOrDefault(property =>
                property.Name == step.Deadline.Date.Trim() && !property.IsArray &&
                property.DataType is DataType.Date or DataType.DateTime);

    public async Task<PostponeDeadlinesResult> Postpone(WorkflowInstance instance, Action action,
        PostponeDeadlinesRequest request, User user, CancellationToken ct)
    {
        var changes = ResolveChanges(instance, request.Changes);
        if (changes == null)
            return new([PostponementError.InvalidChanges]);

        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var limits = changes.Keys
            .Select(property => (Property: property, Days: GetMaxPostponementDays(property, definition)))
            .Where(limit => limit.Days.HasValue)
            .ToDictionary(limit => limit.Property, limit => limit.Days!.Value);
        if (limits.Count > 0)
        {
            var journal = await journalService.GetInstanceJournal(instance.Id, ct: ct);
            foreach (var change in request.Changes)
            {
                if (!limits.TryGetValue(change.Property, out var days)) continue;
                var property = definition.Properties.Single(property => property.Name == change.Property);
                if (GetMaximumDate(property, days, change.PreviousDate, journal) is { } maximum &&
                    change.NewDate > maximum)
                    return new([PostponementError.MaximumExtensionExceeded]);
            }
        }

        foreach (var (property, date) in changes)
        {
            var previous = instance.GetProperty(property);
            instance.SetProperty(new BsonDateTime(date.UtcDateTime), property);
            await instanceService.SaveValue(instance, null, property, ct);
            var entry = PropertyChangeEntry.Create(property, previous, user);
            entry.Reason = request.Reason;
            await journalService.LogPropertyChange(instance.Id, entry, ct);
        }

        await instanceService.UpdateCurrentStep(instance, ct);
        await journalService.IncrementVersion(instance.Id, ct);

        var effects = await jobService.CreateAndRunJob(instance, action, user, null, ct);
        return new([], effects);
    }

    // Resolve and validate every change before saving anything.
    private Dictionary<string, DateTimeOffset>? ResolveChanges(WorkflowInstance instance, DeadlineChange[]? changes)
    {
        if (changes is not { Length: > 0 }) return null;

        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);
        var deadlines = new Dictionary<string, DateTimeOffset?>();
        foreach (var step in definition.AllSteps)
        {
            if (GetDeadlineProperty(step, definition) is { } property)
                deadlines.TryAdd(property.Name, step.Deadline!.Evaluate(context));
        }

        var resolved = new Dictionary<string, DateTimeOffset>();
        foreach (var change in changes)
        {
            if (change == null || string.IsNullOrEmpty(change.Property))
                return null;
            if (!deadlines.TryGetValue(change.Property, out var currentDate) || currentDate == null)
                return null;
            if (change.PreviousDate != currentDate)
                return null;

            var date = ResolveDate(change);
            if (date == null || date <= currentDate)
                return null;
            if (!resolved.TryAdd(change.Property, date.Value))
                return null;
        }

        return resolved;
    }

    public static int? GetMaxPostponementDays(string propertyName, WorkflowDefinition definition)
    {
        var limits = definition.AllSteps
            .Where(step => GetDeadlineProperty(step, definition)?.Name == propertyName)
            .Select(step => step.Deadline!.MaxPostponementDays)
            .Where(days => days.HasValue)
            .Select(days => days!.Value)
            .ToArray();
        return limits.Length == 0 ? null : limits.Min();
    }

    public static DateOnly? GetMaximumDate(PropertyDefinition property, int? maxPostponementDays,
        DateTimeOffset currentDate,
        InstanceJournalEntry? journal)
    {
        if (maxPostponementDays is not { } days) return null;
        // Use this property's first recorded date, independent of the order returned by the journal.
        var first = journal?.PropertyChanges
            .Where(change => change.Path == property.Name && change.OldValue is BsonDateTime)
            .OrderBy(change => change.Timestamp)
            .FirstOrDefault();
        var original = first?.OldValue is BsonDateTime date
            ? new DateTimeOffset(date.ToUniversalTime())
            : currentDate;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var originalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(original, zone).DateTime);
        return originalDate.AddDays(Math.Min(days, DateOnly.MaxValue.DayNumber - originalDate.DayNumber));
    }

    // Preserve the Amsterdam wall-clock time when extending across daylight-saving transitions.
    internal static DateTimeOffset? ResolveDate(DeadlineChange change)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var previous = TimeZoneInfo.ConvertTime(change.PreviousDate, zone);
        var date = change.NewDate.ToDateTime(TimeOnly.FromDateTime(previous.DateTime));
        return zone.IsInvalidTime(date) ? null : new DateTimeOffset(date, zone.GetUtcOffset(date));
    }
}