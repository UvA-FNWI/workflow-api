using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Events;

public class UndoService(
    IInstanceEventRepository eventRepository,
    IWorkflowInstanceRepository workflowInstanceRepository,
    InstanceService instanceService,
    RightsService rightsService)
{
    public async Task<OperationMetadata?> GetCandidate(
        WorkflowInstance instance,
        string topLevelStep,
        CancellationToken ct)
    {
        var eventLogs = await eventRepository.GetEventLogEntriesForInstance(instance.Id, ct);
        return await ResolveCandidate(instance, topLevelStep, eventLogs);
    }

    public Task<OperationMetadata?> GetCandidate(
        WorkflowInstance instance,
        string topLevelStep,
        IEnumerable<InstanceEventLogEntry> eventLogs)
        => ResolveCandidate(instance, topLevelStep, eventLogs);

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
        var operation = EventHistory.FindOperation(eventLogs, operationId);
        if (operation == null)
            throw new UndoCandidateChangedException();
        if (!await IsAuthorized(instance, operation))
            throw new ForbiddenWorkflowActionException(instance.Id, RoleAction.Undo, operation.Source);

        var candidate = await ResolveCandidate(instance, operation.TopLevelStep, eventLogs);
        if (candidate?.Id != operationId || candidate.Revision == null)
            throw new UndoCandidateChangedException();

        if (!await eventRepository.AddUndoEntry(instance.Id, candidate.TopLevelStep, candidate.Id,
                candidate.Revision.Value, realUser, trimmedReason, ct))
            throw new UndoCandidateChangedException();

        var updatedLogs = await eventRepository.GetEventLogEntriesForInstance(instance.Id, ct);
        instance.Events = EventHistory.RebuildEvents(updatedLogs);
        await instanceService.UpdateCurrentStep(instance, ct);
        await workflowInstanceRepository.Update(instance, ct);
        return instance;
    }

    private async Task<OperationMetadata?> ResolveCandidate(
        WorkflowInstance instance,
        string topLevelStep,
        IEnumerable<InstanceEventLogEntry> eventLogs)
    {
        var candidate = EventHistory.LatestOperations(eventLogs).GetValueOrDefault(topLevelStep);
        if (candidate is not { Type: OperationType.FormSubmission })
            return null;

        return await IsAuthorized(instance, candidate) ? candidate : null;
    }

    private async Task<bool> IsAuthorized(WorkflowInstance instance, OperationMetadata operation)
    {
        var allowed = await rightsService.GetAllowedActionsForStep(
            instance, operation.Step, RightsEvaluationMode.RequestContext, RoleAction.Undo);
        return allowed.Any(action => action.AllForms.Length == 0 || action.MatchesForm(operation.Source));
    }
}