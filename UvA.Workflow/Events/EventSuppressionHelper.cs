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

        foreach (var (otherEventId, otherEvent) in instance.Events)
        {
            if (otherEvent.Date == null || otherEventId == eventId)
                continue;

            // Only later events can suppress
            if (otherEvent.Date > evt.Date)
            {
                var otherEventDef = workflowDef.Events.FirstOrDefault(e => e.Name == otherEventId);
                if (otherEventDef?.Suppresses?.Contains(eventId) == true)
                {
                    // Track the most recent suppressing event
                    if (latestSuppressingDate == null || otherEvent.Date > latestSuppressingDate)
                    {
                        suppressingEventId = otherEventId;
                        latestSuppressingDate = otherEvent.Date;
                    }
                }
            }
        }

        return suppressingEventId;
    }
}