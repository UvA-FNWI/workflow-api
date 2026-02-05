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
    IStepVersionService stepVersionService,
    WorkflowInstanceService workflowInstanceService,
    ILogger<WorkflowInstanceDtoFactory> logger)
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
                var versions = await stepVersionService.GetStepVersions(instance, step.Name, ct);
                if (versions.Any())
                {
                    stepVersionsMap[step.Name] = versions;
                }
            }
            catch (Exception ex)
            {
                // If fetching versions fails for a step, continue without versions for that step
                logger.LogError(ex, "Failed to fetch step versions for step {StepName}", step.Name);
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
            versions?.Select(v => CreateStepVersionDto(v, instance)).ToList()
        );
    }

    /// <summary>
    /// Creates a StepVersionDto with properly constructed SubmissionDtos for all events in the version
    /// </summary>
    private StepVersionDto CreateStepVersionDto(StepVersion stepVersion, WorkflowInstance instance)
    {
        try
        {
            var submissions = new List<SubmissionDto>();

            // Get the instance at the version timestamp
            var instanceAtVersion = workflowInstanceService
                .GetAsOfVersion(instance.Id, stepVersion.InstanceVersion, CancellationToken.None).Result;

            // Create a submission for each event in the version
            foreach (var eventId in stepVersion.EventIds)
            {
                // Get the form for this event
                Form? form;
                try
                {
                    form = modelService.GetForm(instanceAtVersion, eventId);
                }
                catch (ArgumentException)
                {
                    logger.LogWarning("Form not found for event {EventId} in version {VersionNumber}",
                        eventId, stepVersion.VersionNumber);
                    continue;
                }

                // Get the submission event from the versioned instance
                var submission = instanceAtVersion.Events.GetValueOrDefault(eventId);

                // Get question status with all fields visible (historical view)
                var questionStatus = modelService.GetQuestionStatus(instanceAtVersion, form, canViewHidden: true);

                // Create the submission DTO with empty permissions (historical view)
                var submissionDto =
                    submissionDtoFactory.Create(instanceAtVersion, form, submission, questionStatus, permissions: []);

                submissions.Add(submissionDto);
            }

            return new StepVersionDto
            {
                VersionNumber = stepVersion.VersionNumber,
                EventIds = stepVersion.EventIds,
                SubmittedAt = stepVersion.SubmittedAt,
                Submissions = submissions
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create StepVersionDto for version {VersionNumber}",
                stepVersion.VersionNumber);
            throw;
        }
    }
}