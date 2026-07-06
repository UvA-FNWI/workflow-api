using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Events;
using UvA.Workflow.Submissions;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;

namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public class WorkflowInstanceDtoFactory(
    InstanceService instanceService,
    ModelService modelService,
    SubmissionDtoFactory submissionDtoFactory,
    IWorkflowInstanceRepository repository,
    RightsService rightsService,
    IStepVersionService stepVersionService,
    StepHeaderStatusResolver stepHeaderStatusResolver,
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
        // Both admin-tool and impersonation visibility are evaluated against the real user (ignoring any
        // active impersonation); resolve them in a single pass over the instance's roles.
        var realUserActions = await rightsService.GetAllowedActions(
            instance,
            RightsEvaluationMode.RealUser,
            RoleAction.ViewAdminTools,
            RoleAction.ImpersonateRoles);
        var canUseAdminTools = realUserActions.Any(a => a.Type == RoleAction.ViewAdminTools);
        var canImpersonate = realUserActions.Any(a => a.Type == RoleAction.ImpersonateRoles);
        var viewerRoles = await rightsService.GetViewerRoles(instance, ct);
        var context = modelService.CreateContext(instance);
        await instanceService.Enrich(workflowDefinition, [context],
            workflowDefinition.Steps.SelectMany(f => f.Lookups), ct);

        // Fetch versions for all steps
        var instanceHistory = await workflowInstanceService.GetInstanceHistory(instance.Id, ct);
        var stepVersionsMap = GetStepVersionsMap(instance, workflowDefinition.AllSteps, instanceHistory.EventLogs);
        var steps = await Task.WhenAll(workflowDefinition.Steps
            .Where(s => s.Condition.IsMet(context))
            .Select(s => CreateStepDto(s, instance, stepVersionsMap, instanceHistory, context, ct)));

        var x = new WorkflowInstanceDto(
            instance.Id,
            workflowDefinition.InstanceTitleTemplate?.Apply(modelService.CreateContext(instance)),
            WorkflowDefinitionDto.Create(modelService.WorkflowDefinitions[instance.WorkflowDefinition]),
            instance.CurrentStep,
            instance.ParentId,
            actions.Select(ActionDto.Create).ToArray(),
            CreateFields(workflowDefinition, instance.Id, ct).Result ?? [],
            steps,
            submissions
                .Select(s => submissionDtoFactory.Create(instance, s.Form, s.SubmissionState, s.QuestionStatus,
                    permissions.Where(p => p.MatchesForm(s.Form.Name)).Select(p => p.Type).ToArray()))
                .ToArray(),
            permissions.Where(a => a.AllForms.Length == 0).Select(a => a.Type).Distinct().ToArray(),
            canUseAdminTools,
            canImpersonate,
            viewerRoles
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
                workflowDefinition.Fields.SelectMany(f => f.Properties), ct);
            foreach (var field in workflowDefinition.Fields)
            {
                var obj = field.GetValue(context);
                if (obj is object[] arr && arr.Length == 1)
                    obj = arr[0];
                var key = field.CurrentStep ? "CurrentStep" : field.Property;
                result.Add(new FieldDto(key, field.DisplayTitle, obj));
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Creates versions for all steps from a preloaded instance-wide event log.
    /// </summary>
    private Dictionary<string, List<StepVersion>> GetStepVersionsMap(
        WorkflowInstance instance,
        IEnumerable<Step> steps,
        IEnumerable<InstanceEventLogEntry> eventLogs)
    {
        var eventLogList = eventLogs.ToList();
        var stepVersionsMap = new Dictionary<string, List<StepVersion>>();

        foreach (var step in steps)
        {
            try
            {
                var versions = stepVersionService.GetStepVersions(instance, step.Name, eventLogList);
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
    private async Task<StepDto> CreateStepDto(
        Step step,
        WorkflowInstance instance,
        Dictionary<string, List<StepVersion>> stepVersionsMap,
        WorkflowInstanceHistory instanceHistory,
        ObjectContext context,
        CancellationToken ct)
    {
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var versions = stepVersionsMap.GetValueOrDefault(step.Name);
        var children = step.Children.Length != 0
            ? await Task.WhenAll(step.Children
                .Where(s => s.Condition.IsMet(context))
                .Select(s => CreateStepDto(s, instance, stepVersionsMap, instanceHistory, context, ct)))
            : null;
        var versionDtos = versions != null
            ? await Task.WhenAll(versions.Select(v => CreateStepVersionDto(v, instance, instanceHistory, ct)))
            : null;

        return new StepDto(
            step.Name,
            step.DisplayTitle,
            step.Icon,
            step.EndEvent,
            step.GetEndDate(instance, workflowDef),
            step.GetDeadline(instance, modelService),
            children,
            stepHeaderStatusResolver.Resolve(step, instance),
            step.ResultsType,
            step.HierarchyMode,
            versionDtos?.ToList()
        );
    }

    /// <summary>
    /// Creates a StepVersionDto with properly constructed SubmissionDtos for all events in the version
    /// </summary>
    private async Task<StepVersionDto> CreateStepVersionDto(
        StepVersion stepVersion,
        WorkflowInstance instance,
        WorkflowInstanceHistory instanceHistory,
        CancellationToken ct)
    {
        try
        {
            var submissions = new List<SubmissionDto>();

            // Get the instance at the version timestamp
            var instanceAtVersion = workflowInstanceService
                .GetAsOfTimestamp(instance, stepVersion.SubmittedAt, instanceHistory);
            var allowedViewActions = await rightsService.GetAllowedActions(instanceAtVersion, RoleAction.View);

            // Create a submission for each event in the version
            foreach (var eventId in stepVersion.EventIds)
            {
                var form = ResolveSubmissionForm(instanceAtVersion, eventId);
                if (form == null)
                {
                    logger.LogWarning("Form not found for event {EventId} in version {VersionNumber}",
                        eventId, stepVersion.VersionNumber);
                    continue;
                }

                if (!allowedViewActions.Any(action => action.MatchesForm(form.Name)))
                    continue;

                // Get question status with all fields visible (historical view)
                var questionStatus = modelService.GetQuestionStatus(instanceAtVersion, form, false);
                var workflowDef = modelService.WorkflowDefinitions[instanceAtVersion.WorkflowDefinition];
                var submissionState = FormSubmissionState.Resolve(instanceAtVersion, form, workflowDef);

                // Create the submission DTO with empty permissions (historical view)
                var submissionDto =
                    submissionDtoFactory.Create(instanceAtVersion, form, submissionState, questionStatus,
                        permissions: []);

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

    private Form? ResolveSubmissionForm(WorkflowInstance instance, string eventId)
    {
        var directForm = modelService.TryGetForm(instance, eventId);
        if (directForm != null)
            return directForm;

        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        return workflowDef.Forms.FirstOrDefault(form =>
            FormSubmissionState.GetSubmissionEventIds(form).Contains(eventId));
    }
}