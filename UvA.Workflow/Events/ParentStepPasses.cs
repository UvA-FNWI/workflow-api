using System.Text;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Events;

internal sealed record ParentStepPass(
    Step Parent,
    Condition Ends,
    HashSet<string> FirstChildEvents,
    HashSet<string> ResetEvents,
    HashSet<string> Suppresses)
{
    internal string MarkerId { get; } =
        ParentStepPasses.MarkerPrefix + Convert.ToHexString(Encoding.UTF8.GetBytes(Parent.Name));

    // Earlier configs could start a pass on any one of these events independently.
    internal bool HasLegacyRestart { get; } = Ends.Part is EventCondition || IsEventAlternatives(Ends);

    private static bool IsEventAlternatives(Condition condition)
        => condition.Part is Or alternatives && alternatives.All(child =>
            child.Part is EventCondition || IsEventAlternatives(child));

    internal bool IsComplete(WorkflowInstance instance, WorkflowDefinition definition)
    {
        var values = new Dictionary<Lookup, object?>();
        foreach (var id in FirstChildEvents)
        {
            if (instance.Events.GetValueOrDefault(id)?.Date is not { } date ||
                !EventSuppressionHelper.IsEventActive(id, instance, definition))
                continue;

            values[id + "Event"] = date;
            values[id + "EventActive"] = true;
        }

        return Ends.IsMet(new ObjectContext(values));
    }

    internal IEnumerable<string> SuppressingEvents(WorkflowInstance instance, string target)
    {
        if (instance.Events.ContainsKey(MarkerId))
            return Suppresses.Contains(target) ? [MarkerId] : [];

        return HasLegacyRestart && (Suppresses.Contains(target) || FirstChildEvents.Contains(target))
            ? FirstChildEvents
            : [];
    }
}

public static class ParentStepPasses
{
    internal const string MarkerPrefix = "__resetPass_";

    internal static bool Watches(WorkflowDefinition definition, string eventId)
        => definition.ParentStepPasses.Any(pass => pass.FirstChildEvents.Contains(eventId) ||
                                                   pass.ResetEvents.Contains(eventId));

    internal static bool IsMarker(string eventId) => eventId.StartsWith(MarkerPrefix, StringComparison.Ordinal);

    // The repository calls this against the current stored event state before committing the changes together.
    public static InstanceEvent[] RecordEvent(WorkflowInstance instance, WorkflowDefinition definition, string eventId)
    {
        var affected = definition.ParentStepPasses.Where(pass => pass.FirstChildEvents.Contains(eventId))
            .Select(pass => (Pass: pass, WasComplete: pass.IsComplete(instance, definition),
                LegacyBoundary: LegacyBoundary(instance, pass)))
            .ToArray();
        var recorded = instance.RecordEvent(eventId);
        var changes = new List<InstanceEvent> { recorded };
        foreach (var (pass, wasComplete, legacyBoundary) in affected)
        {
            var boundary = !wasComplete && pass.IsComplete(instance, definition) ? recorded.Date : legacyBoundary;
            if (boundary == null)
                continue;

            var marker = instance.RecordEvent(pass.MarkerId, boundary);
            changes.Add(marker);
        }

        return changes.ToArray();
    }

    private static DateTime? LegacyBoundary(WorkflowInstance instance, ParentStepPass pass)
        => pass.HasLegacyRestart && !instance.Events.ContainsKey(pass.MarkerId)
            ? pass.FirstChildEvents.Select(id => instance.Events.GetValueOrDefault(id)?.Date).Max()
            : null;
}