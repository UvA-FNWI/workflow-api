using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;

namespace UvA.Workflow.Events;

public interface IInstanceEventService
{
    Task UpdateEvent(WorkflowInstance instance, string eventId, User user, CancellationToken ct);

    /// <summary>
    /// Deletes a specific event from the provided workflow instance based on the given event ID.
    /// </summary>
    /// <param name="instance">The workflow instance from which the event will be deleted.</param>
    /// <param name="eventId">The unique identifier of the event to be deleted.</param>
    /// <param name="user">The user performing the delete action.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <exception cref="EntityNotFoundException">
    /// Thrown when the specified event ID is not found within the workflow instance.
    /// </exception>
    Task DeleteEvent(WorkflowInstance instance, string eventId, User user, CancellationToken ct);

    /// <summary>
    /// Determines whether a specific event has ever been triggered in the specified workflow instance.
    /// Will return true if an event was triggered and later deleted.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the workflow instance to check.</param>
    /// <param name="eventId">The unique identifier of the event to verify.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>True if the event has been triggered at least once in the specified instance; otherwise, false.</returns>
    Task<bool> WasEventEverTriggered(string instanceId, string eventId, CancellationToken ct = default);
}

public class InstanceEventService(
    IInstanceEventRepository eventRepository,
    IInstanceJournalService instanceJournalService,
    RightsService rightsService,
    InstanceService instanceService) : IInstanceEventService
{
    public async Task UpdateEvent(WorkflowInstance instance, string eventId, User user, CancellationToken ct)
    {
        var newEvent = instance.RecordEvent(eventId);
        await eventRepository.AddOrUpdateEvent(instance, newEvent, user, ct);
    }

    /// <summary>
    /// Deletes a specific event from the provided workflow instance based on the given event ID.
    /// </summary>
    /// <param name="instance">The workflow instance from which the event will be deleted.</param>
    /// <param name="eventId">The unique identifier of the event to be deleted.</param>
    /// <param name="user">The user performing the delete action.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <exception cref="EntityNotFoundException">
    /// Thrown when the specified event ID is not found within the workflow instance.
    /// </exception>
    public async Task DeleteEvent(WorkflowInstance instance, string eventId, User user, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(instance, RoleAction.ViewAdminTools);

        if (instance.Events.TryGetValue(eventId, out InstanceEvent? instanceEvent))
        {
            await eventRepository.DeleteEvent(instance, instanceEvent, user, ct);
            instance.Events.Remove(eventId);
            await instanceJournalService.IncrementVersion(instance.Id, ct);
            await instanceService.UpdateCurrentStep(instance, ct);
        }
        else
            throw new EntityNotFoundException(nameof(InstanceEvent), eventId);
    }

    /// <summary>
    /// Determines whether a specific event has ever been triggered in the specified workflow instance.
    /// Will return true if an event was triggered and later deleted.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the workflow instance to check.</param>
    /// <param name="eventId">The unique identifier of the event to verify.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>True if the event has been triggered at least once in the specified instance; otherwise, false.</returns>
    public async Task<bool> WasEventEverTriggered(string instanceId, string eventId, CancellationToken ct = default)
        => await eventRepository.CountEventLogFor(instanceId, eventId, ct) > 0;
}