using UvA.Workflow.Infrastructure;
using UvA.Workflow.Jobs;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Events;

public class UndoService(
    IInstanceEventRepository eventRepository,
    IJobRepository jobRepository,
    IWorkflowInstanceRepository workflowInstanceRepository,
    InstanceService instanceService,
    RightsService rightsService,
    ModelService modelService)
{
    public async Task<InstanceEventLogEntry?> GetCandidate(
        WorkflowInstance instance,
        string topLevelStep,
        IEnumerable<InstanceEventLogEntry> eventLogs)
    {
        var candidate = EventHistory.LatestOperations(
            eventLogs, modelService.WorkflowDefinitions[instance.WorkflowDefinition]).GetValueOrDefault(topLevelStep);
        return candidate?.OperationMetadata != null && await IsAuthorized(instance, candidate.OperationMetadata)
            ? candidate
            : null;
    }

    public async Task<WorkflowInstance> Undo(
        WorkflowInstance instance,
        string operationId,
        string reason,
        User realUser,
        CancellationToken ct)
    {
        var trimmedReason = reason.Trim();
        if (trimmedReason.Length == 0 || trimmedReason.Length > 1000)
            throw new ArgumentException("Undo reason must contain between 1 and 1,000 characters.", nameof(reason));

        var eventLogs = await eventRepository.GetEventLogEntriesForInstance(instance.Id, ct);
        var operation = eventLogs.FirstOrDefault(log => log.OperationMetadata?.Id == operationId)?.OperationMetadata;
        if (operation == null)
            throw new UndoCandidateChangedException();
        if (!await IsAuthorized(instance, operation))
            throw new ForbiddenWorkflowActionException(instance.Id, RoleAction.Undo, operation.Source);

        var isCandidate = EventHistory.LatestOperations(
                eventLogs, modelService.WorkflowDefinitions[instance.WorkflowDefinition])
            .Values
            .Any(log => log.OperationMetadata?.Id == operationId);
        if (!isCandidate)
            throw new UndoCandidateChangedException();

        var affectedEventIds = eventLogs
            .Where(log => log.OperationId == operationId && log.Operation != EventLogOperation.Undo)
            .Select(log => log.EventId)
            .Distinct()
            .ToArray();

        await eventRepository.AddUndoEntry(instance.Id, operationId, realUser, trimmedReason, ct);

        await jobRepository.CancelPendingForOperation(instance.Id, operationId, ct);

        var updatedLogs = await eventRepository.GetEventLogEntriesForInstance(instance.Id, ct);
        var effectiveEvents = EventHistory.RebuildEvents(updatedLogs);
        var updates = new List<UpdateDefinition<WorkflowInstance>>();
        foreach (var eventId in affectedEventIds)
        {
            if (effectiveEvents.TryGetValue(eventId, out var effectiveEvent))
            {
                instance.Events[eventId] = effectiveEvent;
                updates.Add(Builders<WorkflowInstance>.Update.Set(item => item.Events[eventId], effectiveEvent));
            }
            else
            {
                instance.Events.Remove(eventId);
                updates.Add(Builders<WorkflowInstance>.Update.Unset(item => item.Events[eventId]));
            }
        }

        await instanceService.UpdateCurrentStep(instance, ct);
        await workflowInstanceRepository.UpdateFields(
            instance.Id, Builders<WorkflowInstance>.Update.Combine(updates), ct);
        return instance;
    }

    private async Task<bool> IsAuthorized(WorkflowInstance instance, OperationMetadata operation)
    {
        var allowed = await rightsService.GetAllowedActionsForStep(
            instance, operation.Step, RoleAction.Undo);
        return allowed.Any(action => operation.Type switch
        {
            OperationType.FormSubmission =>
                action.MatchesForm(operation.Source) || action.AllForms.Length == 0 && action.Name == null,
            OperationType.ExecuteAction =>
                action.Name == operation.Source || action.Name == null && action.AllForms.Length == 0,
            _ => false
        });
    }
}