using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users.Dtos;
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
        var visibleCards = workflowDefinition.InfoCards
            .Where(card => card.Enabled && card.Type != null &&
                           (card.Sources is not { Length: > 0 } || card.Sources.Intersect(viewerRoles).Any()))
            .ToArray();
        await instanceService.Enrich(workflowDefinition, [context],
            workflowDefinition.Steps.SelectMany(f => f.Lookups)
                .Concat(visibleCards.SelectMany(card => card.Properties)),
            ct);

        // Fetch versions for all steps
        var instanceHistory = await workflowInstanceService.GetInstanceHistory(instance.Id, ct);
        var displayNames = await submissionDtoFactory.ResolveDisplayNames(instanceHistory.Journal, ct);
        var stepVersionsMap = GetStepVersionsMap(instance, workflowDefinition.AllSteps, instanceHistory.EventLogs);
        var activeSteps = modelService.GetActiveSteps(instance).ToHashSet();
        var steps = await Task.WhenAll(workflowDefinition.Steps
            .Where(s => s.Condition.IsMet(context))
            .Select(s => CreateStepDto(s, instance, stepVersionsMap, instanceHistory, context, activeSteps, ct)));

        var editActions = permissions.Where(a => a.Type == RoleAction.Edit).ToArray();
        var canEditByProperty = rightsService.CanEditProperties(
            instance,
            workflowDefinition.EditableRelatedUsers.Select(r => r.Property),
            editActions);
        var infoCards = visibleCards
            .Select(card => CreateInfoCard(card, context, canEditByProperty))
            .OfType<InfoCardDto>()
            .ToArray();
        var fields = await CreateFields(workflowDefinition, instance, ct);
        var x = new WorkflowInstanceDto(
            instance.Id,
            workflowDefinition.InstanceTitleTemplate?.Apply(modelService.CreateContext(instance)),
            WorkflowDefinitionDto.Create(modelService.WorkflowDefinitions[instance.WorkflowDefinition]),
            instance.CurrentStep,
            instance.ParentId,
            actions.Select(ActionDto.Create).ToArray(),
            fields,
            steps,
            submissions
                .Select(s => submissionDtoFactory.Create(instance, s.Form, s.SubmissionState, s.QuestionStatus,
                    permissions.Where(p => p.MatchesForm(s.Form.Name)).Select(p => p.Type).ToArray(),
                    instanceHistory.Journal, displayNames))
                .ToArray(),
            permissions.Where(a => a.AllForms.Length == 0 && a.PropertyDefinition == null).Select(a => a.Type)
                .Distinct().ToArray(),
            canUseAdminTools,
            canImpersonate,
            viewerRoles,
            infoCards
        );
        return x;
    }

    private async Task<FieldDto[]> CreateFields(WorkflowDefinition workflowDefinition, WorkflowInstance instance,
        CancellationToken ct)
    {
        var result = new List<FieldDto>();
        var context = ObjectContext.Create(instance, modelService);
        await instanceService.Enrich(workflowDefinition, [context],
            workflowDefinition.Fields.SelectMany(f => f.Properties), ct);
        foreach (var field in workflowDefinition.Fields)
        {
            if (!field.Condition.IsMet(context))
                continue;

            var obj = field.GetValue(context);
            if (obj is object[] arr && arr.Length == 1)
                obj = arr[0];
            var key = field.CurrentStep ? "CurrentStep" : field.Property;
            result.Add(new FieldDto(key, field.DisplayTitle, obj, field.IsHighlighted ?? false, field.Order));
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
                var versions = stepVersionService.GetStepVersions(instance, step, eventLogList);
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
        HashSet<string> activeSteps,
        CancellationToken ct)
    {
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        // The newest version is the step's live state, already rendered as the current submission.
        var versions = stepVersionsMap.GetValueOrDefault(step.Name)
            ?.OrderByDescending(version => version.SubmittedAt)
            .Skip(step.HasEnded(context) ? 1 : 0);

        var children = step.Children.Length != 0
            ? await Task.WhenAll(step.Children
                .Where(s => s.Condition.IsMet(context))
                .Select(s => CreateStepDto(s, instance, stepVersionsMap, instanceHistory, context, activeSteps, ct)))
            : null;
        var versionDtos = versions != null
            ? await Task.WhenAll(versions
                .OrderByDescending(version => version.SubmittedAt)
                .Select(version => CreateStepVersionDto(version, instance, instanceHistory, ct)))
            : null;

        var submissionForms = step.Actions
            .Where(action => action.Type == RoleAction.Submit)
            .SelectMany(action => action.AllForms)
            .Distinct()
            .Select(formName => modelService.GetForm(instance, formName))
            .ToArray();
        var submissionEventIds = submissionForms
            .SelectMany(FormSubmissionState.GetSubmissionEventIds)
            .ToHashSet();
        var hasSubmission = submissionForms.Any(form =>
                                FormSubmissionState.Resolve(instance, form, workflowDef).IsSubmitted) ||
                            instanceHistory.EventLogs.Any(log =>
                                submissionEventIds.Contains(log.EventId) &&
                                log.Operation is EventLogOperation.Create or EventLogOperation.Update);
        var expectsSubmission = activeSteps.Contains(step.Name) && step.Actions
            .Where(action => action.Type == RoleAction.Submit && action.Condition.IsMet(context))
            .SelectMany(action => action.AllForms)
            .Distinct()
            .Select(formName => modelService.GetForm(instance, formName))
            .Any(form => !FormSubmissionState.Resolve(instance, form, workflowDef).IsSubmitted);

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
            expectsSubmission,
            hasSubmission,
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

    private InfoCardDto? CreateInfoCard(InfoCard card, ObjectContext context,
        Dictionary<string, bool> canEditByProperty)
    {
        var type = card.Type!.Value;
        if (type == InfoCardType.User)
        {
            var user = context.Get(card.User!) switch
            {
                InstanceUser value => value,
                InstanceUser[] values => values.FirstOrDefault(),
                _ => null
            };
            return new InfoCardDto(
                card.Name,
                card.Title!,
                type,
                user == null ? null : new InfoCardUserDto(user.DisplayName, user.Picture),
                card.Fields.Select(field => CreateInfoCardField(field, context)).OfType<InfoCardFieldDto>().ToArray(),
                card.EmptyText);
        }

        if (type == InfoCardType.RelatedUsers)
        {
            var groups = card.Groups
                .Select(group => new RelatedUserGroupDto(
                    group.Name,
                    group.Title,
                    group.Users.Select(relatedUser => CreateRelatedUser(
                            relatedUser, group.AllowEditing, context, canEditByProperty))
                        .Where(role => role.Users.Length > 0 || role.AllowsAssignment)
                        .ToArray()))
                .Where(group => group.UserRoles.Length > 0)
                .ToArray();
            var items = CreateInfoCardItems(card, context);
            return groups.Length == 0 && items.Length == 0
                ? null
                : new InfoCardDto(card.Name, card.Title!, type, Groups: groups, Items: items);
        }

        if (type == InfoCardType.Links)
        {
            var items = CreateInfoCardItems(card, context);
            return items.Length == 0 ? null : new InfoCardDto(card.Name, card.Title!, type, Items: items);
        }

        return new InfoCardDto(card.Name, card.Title!, type, Content: card.Content);
    }

    private static InfoCardItemDto[] CreateInfoCardItems(InfoCard card, ObjectContext context) =>
        card.Items.Select(item => InfoCardItemDto.TryCreate(item, context))
            .OfType<InfoCardItemDto>()
            .ToArray();

    private static InfoCardFieldDto? CreateInfoCardField(InfoCardField field, ObjectContext context)
    {
        var value = field.GetValue(context);
        if (value is object[] values)
            value = values.Length == 1 ? values[0] : values;
        return value == null || value is string text && string.IsNullOrWhiteSpace(text) ||
               value is Array { Length: 0 }
            ? null
            : new InfoCardFieldDto(field.DisplayTitle, value, field.GetHref(context), field.Icon);
    }

    private static RelatedUserRolesDto CreateRelatedUser(RelatedUser relatedUser, bool allowEditing,
        ObjectContext context, Dictionary<string, bool> canEditByProperty)
    {
        var value = context.Get(relatedUser.Property);
        var users = value is InstanceUser user ? [user] : value as InstanceUser[] ?? [];
        return new RelatedUserRolesDto(
            relatedUser.Property,
            relatedUser.DisplayTitle,
            users.Select(UserDto.CreateFromInstanceUser).ToArray(),
            relatedUser.PropertyDefinition?.AllowsExternalUsers ?? false,
            !relatedUser.PropertyDefinition?.IsRequired ?? false,
            relatedUser.PropertyDefinition?.IsArray ?? false,
            allowEditing && canEditByProperty.GetValueOrDefault(relatedUser.Property));
    }
}