namespace UvA.Workflow.Events;

public interface IInstanceEventRepository
{
    /// <summary>
    /// Adds a new event to a workflow instance or updates an existing event if it already exists.
    /// Logs the operation specifying whether it was an addition or update.
    /// </summary>
    /// <param name="instance">The workflow instance in which the event should be added or updated.</param>
    /// <param name="newEvent">The new event to add or the existing event to update.</param>
    /// <param name="user">The user initiating the add or update operation.</param>
    /// <param name="ct">The cancellation token used to observe the operation's cancellation.</param>
    /// <returns>An asynchronous operation representing the add or update process.</returns>
    Task AddOrUpdateEvent(WorkflowInstance instance, InstanceEvent newEvent, User user,
        CancellationToken ct);

    /// <summary>
    /// Deletes a specified event from a workflow instance and logs the deletion.
    /// </summary>
    /// <param name="instance">The workflow instance from which the event is to be deleted.</param>
    /// <param name="eventToDelete">The event to remove from the instance.</param>
    /// <param name="user">The user executing the deletion action.</param>
    /// <param name="ct">The cancellation token used to observe the operation's cancellation.</param>
    /// <returns>An asynchronous operation representing the deletion process.</returns>
    Task DeleteEvent(WorkflowInstance instance, InstanceEvent eventToDelete, User user,
        CancellationToken ct);

    /// <summary>
    /// Counts the number of event log entries for a specified instance and event.
    /// </summary>
    /// <param name="instanceId">The identifier of the workflow instance to filter the event logs by.</param>
    /// <param name="eventId">The identifier of the event to filter the event logs by.</param>
    /// <param name="ct">The cancellation token used to observe the operation's cancellation.</param>
    /// <returns>The total count of event log entries that match the specified instance ID and event ID.</returns>
    Task<long> CountEventLogFor(string instanceId, string eventId, CancellationToken ct);

    /// <summary>
    /// Adds an event log entry to the event log collection
    /// </summary>
    Task AddEventLogEntry(WorkflowInstance instance, InstanceEvent instanceEvent, User user,
        EventLogOperation operation, CancellationToken ct);

    /// <summary>
    /// Gets all event log entries for specific events in an instance
    /// </summary>
    Task<List<InstanceEventLogEntry>> GetEventLogEntriesForInstance(
        string instanceId,
        List<string> eventIds,
        CancellationToken ct);
}