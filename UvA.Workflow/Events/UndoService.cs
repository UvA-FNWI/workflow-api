using UvA.Workflow.Infrastructure;
using UvA.Workflow.Jobs;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Events;

public class UndoService(
    IInstanceEventRepository eventRepository,
    IJobRepository jobRepository,
    IWorkflowInstanceRepository workflowInstanceRepository,
    InstanceService instanceService,
    RightsService rightsService)
{
    public Task<InstanceEventLogEntry?> GetCandidate(
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
        var operation = EventHistory.FindOperation(eventLogs, operationId)?.OperationMetadata;
        if (operation == null)
            throw new UndoCandidateChangedException();
        if (!await IsAuthorized(instance, operation))
            throw new ForbiddenWorkflowActionException(instance.Id, RoleAction.Undo, operation.Source);

        var candidate = await ResolveCandidate(instance, operation.TopLevelStep, eventLogs);
        if (candidate?.OperationMetadata?.Id != operationId)
            throw new UndoCandidateChangedException();

        await eventRepository.AddUndoEntry(instance.Id, candidate.OperationMetadata.Id, realUser, trimmedReason, ct);

        await jobRepository.CancelPendingForOperation(instance.Id, candidate.OperationMetadata.Id, ct);

        var updatedLogs = await eventRepository.GetEventLogEntriesForInstance(instance.Id, ct);
        instance.Events = EventHistory.RebuildEvents(updatedLogs);
        await instanceService.UpdateCurrentStep(instance, ct);
        await workflowInstanceRepository.Update(instance, ct);
        return instance;
    }

    private async Task<InstanceEventLogEntry?> ResolveCandidate(
        WorkflowInstance instance,
        string topLevelStep,
        IEnumerable<InstanceEventLogEntry> eventLogs)
    {
        var candidate = EventHistory.LatestOperations(eventLogs).GetValueOrDefault(topLevelStep);
        return candidate?.OperationMetadata != null && await IsAuthorized(instance, candidate.OperationMetadata)
            ? candidate
            : null;
    }

    private async Task<bool> IsAuthorized(WorkflowInstance instance, OperationMetadata operation)
    {
        var allowed = await rightsService.GetAllowedActionsForStep(
            instance, operation.Step, RightsEvaluationMode.RequestContext, RoleAction.Undo);
        return allowed.Any(action => operation.Type switch
        {
            OperationType.FormSubmission => action.AllForms.Length == 0 || action.MatchesForm(operation.Source),
            OperationType.ExecuteAction => action.Name == null || action.Name == operation.Source,
            _ => false
        });
    }
}