using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Events;

/// <summary>
/// Helper class to compute event suppression from event history
/// </summary>
public static class EventSuppressionHelper
{
    /// <summary>
    /// Determines if an event is currently active (not suppressed by any later event)
    /// </summary>
    public static bool IsEventActive(
        string eventId,
        WorkflowInstance instance,
        WorkflowDefinition workflowDef)
    {
        return GetSuppressedBy(eventId, instance, workflowDef) == null;
    }

    /// <summary>
    /// Gets the ID of the event that suppresses the given event, if any
    /// </summary>
    public static string? GetSuppressedBy(
        string eventId,
        WorkflowInstance instance,
        WorkflowDefinition workflowDef)
    {
        if (!instance.Events.TryGetValue(eventId, out var evt) || evt.Date == null)
            return null;

        // Find the most recent event that suppresses this one
        string? suppressingEventId = null;
        DateTime? latestSuppressingDate = null;

        foreach (var otherEvent in workflowDef.Events.Where(e => e.Suppresses?.Contains(eventId) == true))
            Consider(otherEvent.Name);

        foreach (var pass in workflowDef.ParentStepPasses)
        foreach (var source in pass.SuppressingEvents(instance, eventId))
            Consider(source);

        return suppressingEventId;

        void Consider(string source)
        {
            if (source != eventId && instance.Events.GetValueOrDefault(source)?.Date is { } date &&
                date > evt.Date && (latestSuppressingDate == null || date > latestSuppressingDate))
            {
                suppressingEventId = source;
                latestSuppressingDate = date;
            }
        }
    }
}