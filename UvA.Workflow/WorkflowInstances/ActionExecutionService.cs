using UvA.Workflow.Events;
using UvA.Workflow.Jobs;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowModel;
using DomainAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.WorkflowInstances;

public class ActionExecutionService(
    EffectService effectService,
    JobService jobService,
    InstanceService instanceService,
    ModelService modelService)
{
    public async Task<EffectResult> Execute(WorkflowInstance instance, DomainAction action, User realUser,
        JobInput? input, CancellationToken ct)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var operation = OperationMetadata.CreateForAction(action, definition);

        // Single and bulk actions must record the same operation and run the same effects.
        await effectService.AddEvent(instance, action.Name!, realUser, ct, operation?.Id, operation);
        var result = await jobService.CreateAndRunJob(instance, action, realUser, input, ct, operation);
        await instanceService.UpdateCurrentStep(instance, ct);
        return result;
    }
}