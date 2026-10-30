using UvA.Workflow.Api.Screens.Dtos;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Screens;

public class BulkActionsService(
    ModelService modelService,
    IWorkflowInstanceRepository repository,
    RightsService rightsService,
    ActionExecutionService actionExecutionService,
    ILogger<BulkActionsService> logger)
{
    public Task<BulkActionResultDto> Preview(string workflowDefinition, string screenName, string actionName,
        string[] instanceIds, CancellationToken ct)
        => Process(workflowDefinition, screenName, actionName, instanceIds, null, ct);

    public Task<BulkActionResultDto> Execute(string workflowDefinition, string screenName, string actionName,
        string[] instanceIds, User realUser, CancellationToken ct)
        => Process(workflowDefinition, screenName, actionName, instanceIds, realUser, ct);

    private async Task<BulkActionResultDto> Process(string workflowDefinition, string screenName, string actionName,
        string[] instanceIds, User? realUser, CancellationToken ct)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition) ||
            definition.Screens.GetOrDefault(screenName) is not { } screen ||
            !screen.BulkActions.Any(a => a.Action == actionName))
            throw new ArgumentException("Bulk action is not configured for this screen.");

        var rows = new List<BulkActionRowDto>();
        foreach (var id in instanceIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var instance = await repository.GetById(id, ct);
                if (instance == null || instance.WorkflowDefinition != workflowDefinition ||
                    !await rightsService.Can(instance, RoleAction.View))
                {
                    rows.Add(new(id, BulkActionStatus.Unavailable));
                    continue;
                }

                var action = (await rightsService.GetAllowedActions(instance, RoleAction.Execute))
                    .FirstOrDefault(a => a.Name == actionName);
                if (action == null || action.OnAction.Any(e => e.SendMail?.SendAutomatically == false ||
                                                               e.SendAccessMail?.SendAutomatically == false))
                {
                    rows.Add(new(id, BulkActionStatus.Unavailable));
                    continue;
                }

                var status = BulkActionStatus.Eligible;
                if (realUser != null)
                {
                    var result = await actionExecutionService.Execute(instance, action, realUser, null, ct);
                    status = result.Error == null ? BulkActionStatus.Succeeded : BulkActionStatus.Failed;
                }

                rows.Add(new(id, status));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Bulk action {Action} failed for instance {InstanceId}", actionName, id);
                rows.Add(new(id, BulkActionStatus.Failed));
            }
        }

        return new(rows.ToArray());
    }
}