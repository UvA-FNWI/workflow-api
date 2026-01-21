using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class WorkflowInstanceDtoFactory(
    InstanceService instanceService,
    ModelService modelService,
    SubmissionDtoFactory submissionDtoFactory,
    IWorkflowInstanceRepository repository,
    RightsService rightsService,
    IStepVersionService stepVersionService)
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

        // Fetch versions for all steps
        var stepVersionsMap = await GetStepVersionsMap(instance, workflowDefinition.AllSteps, ct);

        var x = new WorkflowInstanceDto(
            instance.Id,
            workflowDefinition.InstanceTitleTemplate?.Apply(modelService.CreateContext(instance)),
            WorkflowDefinitionDto.Create(modelService.WorkflowDefinitions[instance.WorkflowDefinition]),
            instance.CurrentStep,
            instance.ParentId,
            actions.Select(ActionDto.Create).ToArray(),
            CreateFields(workflowDefinition, instance.Id, ct).Result ?? [],
            workflowDefinition.Steps.Select(s => CreateStepDto(s, instance, stepVersionsMap)).ToArray(),
            submissions
                .Select(s => submissionDtoFactory.Create(instance, s.Form, s.Event, s.QuestionStatus,
                    permissions.Where(p => p.MatchesForm(s.Form.Name)).Select(p => p.Type).ToArray()))
                .ToArray(),
            permissions.Where(a => a.AllForms.Length == 0).Select(a => a.Type).Distinct().ToArray()
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

    /// <summary>
    /// Fetches versions for all steps and returns a dictionary keyed by step name
    /// </summary>
    private async Task<Dictionary<string, List<StepVersion>>> GetStepVersionsMap(
        WorkflowInstance instance,
        IEnumerable<Step> steps,
        CancellationToken ct)
    {
        var stepVersionsMap = new Dictionary<string, List<StepVersion>>();

        foreach (var step in steps)
        {
            try
            {
                var versionsResponse = await stepVersionService.GetStepVersions(instance, step.Name, ct);
                if (versionsResponse.Versions.Any())
                {
                    stepVersionsMap[step.Name] = versionsResponse.Versions;
                }
            }
            catch
            {
                // If fetching versions fails for a step, continue without versions for that step
            }
        }

        return stepVersionsMap;
    }

    /// <summary>
    /// Creates a StepDto with versions from the map, recursively handling child steps
    /// </summary>
    private StepDto CreateStepDto(
        Step step,
        WorkflowInstance instance,
        Dictionary<string, List<StepVersion>> stepVersionsMap)
    {
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var versions = stepVersionsMap.GetValueOrDefault(step.Name);

        return new StepDto(
            step.Name,
            step.DisplayTitle,
            step.EndEvent,
            step.GetEndDate(instance, workflowDef),
            step.GetDeadline(instance, modelService),
            step.Children.Length != 0
                ? step.Children.Select(s => CreateStepDto(s, instance, stepVersionsMap)).ToArray()
                : null,
            versions
        );
    }
}