using UvA.Workflow.Infrastructure;

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
}

public class InstanceEventService(
    IInstanceEventRepository eventRepository,
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
            await instanceService.UpdateCurrentStep(instance, ct);
        }
        else
            throw new EntityNotFoundException(nameof(InstanceEvent), eventId);
    }
}