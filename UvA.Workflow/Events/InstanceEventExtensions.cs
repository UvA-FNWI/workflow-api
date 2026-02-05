using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Events;

/// <summary>
/// Extension methods for working with InstanceEvent collections
/// </summary>
public static class InstanceEventExtensions
{
    /// <summary>
    /// Filters the event collection to only include active (non-suppressed) events
    /// </summary>
    /// <param name="events">Collection of events to filter</param>
    /// <param name="instance">The workflow instance containing the events</param>
    /// <param name="workflowDef">The workflow definition with suppression rules</param>
    /// <returns>Only events that are currently active (not suppressed by later events)</returns>
    public static IEnumerable<InstanceEvent> WhereActive(
        this IEnumerable<InstanceEvent> events,
        WorkflowInstance instance,
        WorkflowDefinition workflowDef)
    {
        return events.Where(evt => EventSuppressionHelper.IsEventActive(evt.Id, instance, workflowDef));
    }

    /// <summary>
    /// Filters the event dictionary to only include active (non-suppressed) events
    /// </summary>
    /// <param name="events">Dictionary of events to filter</param>
    /// <param name="instance">The workflow instance containing the events</param>
    /// <param name="workflowDef">The workflow definition with suppression rules</param>
    /// <returns>Key-value pairs where the event is currently active (not suppressed)</returns>
    public static IEnumerable<KeyValuePair<string, InstanceEvent>> WhereActive(
        this IEnumerable<KeyValuePair<string, InstanceEvent>> events,
        WorkflowInstance instance,
        WorkflowDefinition workflowDef)
    {
        return events.Where(kvp => EventSuppressionHelper.IsEventActive(kvp.Key, instance, workflowDef));
    }
}