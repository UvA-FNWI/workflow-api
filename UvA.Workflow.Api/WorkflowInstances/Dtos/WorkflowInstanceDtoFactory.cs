using UvA.Workflow.Api.Screens;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class WorkflowInstanceDtoFactory(
    InstanceService instanceService,
    ModelService modelService,
    SubmissionDtoFactory submissionDtoFactory,
    IWorkflowInstanceRepository repository,
    ScreenDataService screenDataService,
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
        var permissions = await rightsService.GetAllowedActions(instance, RoleAction.ViewAdminTools);

        return new WorkflowInstanceDto(
            instance.Id,
            workflowDefinition.InstanceTitleTemplate?.Apply(modelService.CreateContext(instance)),
            WorkflowDefinitionDto.Create(modelService.WorkflowDefinitions[instance.WorkflowDefinition]),
            instance.CurrentStep,
            instance.ParentId,
            actions.Select(ActionDto.Create).ToArray(),
            CreateFields(workflowDefinition, instance.Id, ct).Result ?? [],
            workflowDefinition.Steps.Select(s => StepDto.Create(s, instance, modelService)).ToArray(),
            submissions
                .Select(s => submissionDtoFactory.Create(instance, s.Form, s.Event, s.QuestionStatus))
                .ToArray(),
            permissions.Select(a => a.Type).Distinct().ToArray()
        );
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
                workflowDefinition.Steps.SelectMany(s => s.Lookups), ct);
            foreach (var field in workflowDefinition.HeaderFields)
            {
                var obj = screenDataService.ProcessColumnValue(instance.Properties, field, workflowDefinition.Name,
                    instanceId);
                result.Add(new FieldDto(field.DisplayTitle, obj));
            }
        }

        return result.ToArray();
    }
}