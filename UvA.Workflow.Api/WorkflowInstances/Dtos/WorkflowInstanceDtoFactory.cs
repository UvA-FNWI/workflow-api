using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class WorkflowInstanceDtoFactory(
    InstanceService instanceService,
    ModelService modelService,
    SubmissionDtoFactory submissionDtoFactory,
    IWorkflowInstanceRepository repository,
    RightsService rightsService)
{
    /// <summary>
    /// Creates a WorkflowInstanceDto from a WorkflowInstance domain entity
    /// </summary>
    public async Task<WorkflowInstanceDto> Create(WorkflowInstance instance, CancellationToken ct)
    {
        var actions = await instanceService.GetAllowedActions(instance, ct);
        var submissions = await instanceService.GetAllowedSubmissions(instance, ct);
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var permissions = await rightsService.GetAllowedActions(instance, RoleAction.ViewAdminTools, RoleAction.Edit);
        var canUseAdminTools = await rightsService.Can(
            instance,
            RoleAction.ViewAdminTools,
            RightsEvaluationMode.RealUser);

        var x = new WorkflowInstanceDto(
            instance.Id,
            workflowDefinition.InstanceTitleTemplate?.Apply(modelService.CreateContext(instance)),
            WorkflowDefinitionDto.Create(modelService.WorkflowDefinitions[instance.WorkflowDefinition]),
            instance.CurrentStep,
            instance.ParentId,
            actions.Select(ActionDto.Create).ToArray(),
            CreateFields(workflowDefinition, instance.Id, ct).Result ?? [],
            workflowDefinition.Steps.Select(s => StepDto.Create(s, instance, modelService)).ToArray(),
            submissions
                .Select(s => submissionDtoFactory.Create(instance, s.Form, s.Event, s.QuestionStatus,
                    permissions.Where(p => p.MatchesForm(s.Form.Name)).Select(p => p.Type).ToArray()))
                .ToArray(),
            permissions.Where(a => a.AllForms.Length == 0).Select(a => a.Type).Distinct().ToArray(),
            canUseAdminTools
        );
        return x;
    }

    private async Task<FieldDto[]> CreateFields(WorkflowDefinition workflowDefinition, string instanceId,
        CancellationToken ct)
    {
        var result = new List<FieldDto>();
        var instance = await repository.GetById(instanceId, ct);
        if (instance is not null)
        {
            var context = ObjectContext.Create(instance, modelService);
            await instanceService.Enrich(workflowDefinition, [context],
                workflowDefinition.HeaderFields.SelectMany(f => f.Properties), ct);
            foreach (var field in workflowDefinition.HeaderFields)
            {
                var obj = field.GetValue(context);
                result.Add(new FieldDto(field.DisplayTitle, obj));
            }
        }

        return result.ToArray();
    }
}