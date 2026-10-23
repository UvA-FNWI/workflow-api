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
    StepProgressResolver stepProgressResolver,
    WorkflowInstanceService workflowInstanceService,
    ILogger<WorkflowInstanceDtoFactory> logger,
    UndoService undoService)
{
    /// <summary>
    /// Creates a WorkflowInstanceDto from a WorkflowInstance domain entity
    /// </summary>
    public async Task<WorkflowInstanceDto> Create(WorkflowInstance instance, CancellationToken ct)
    {
        var actions = await instanceService.GetAllowedActions(instance, ct);
        var submissions = await instanceService.GetAllowedSubmissions(instance, ct);
        var workflowDefinition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var permissions = await rightsService.GetAllowedActions(instance, RoleAction.ViewAdminTools, RoleAction.Edit,
            RoleAction.ViewCorrespondence);
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
                           (card.Sources is not { Length: > 0 } || card.Sources.Intersect(viewerRoles).Any()) &&
                           (card.ExcludedSources is not { Length: > 0 } ||
                            !card.ExcludedSources.Intersect(viewerRoles).Any()))
            .ToArray();
        await instanceService.Enrich(workflowDefinition, [context],
            workflowDefinition.Steps.SelectMany(f => f.Lookups)
                .Concat(visibleCards.SelectMany(card => card.Properties)),
            ct);

        // Fetch versions for all steps
        var instanceHistory = await workflowInstanceService.GetInstanceHistory(instance.Id, ct);
        var displayNames = await submissionDtoFactory.ResolveDisplayNames(instanceHistory.Journal, ct);
        var submissionDtos = await Task.WhenAll(submissions.Select(async s =>
        {
            var submissionContext = modelService.CreateContext(instance);
            await instanceService.Enrich(workflowDefinition, [submissionContext], s.Form.ActualForm.Lookups, ct,
                replaceStep: false);
            var dto = submissionDtoFactory.Create(instance, s.Form, s.SubmissionState, s.QuestionStatus,
                permissions.Where(p => p.MatchesForm(s.Form.Name)).Select(p => p.Type).ToArray(),
                instanceHistory, displayNames, submissionContext);
            return dto with
            {
                Form = dto.Form with { Step = StepVersionService.ResolveFormStep(workflowDefinition, s.Form)?.Name }
            };
        }));
        Dictionary<string, StepVersions> stepVersionsMap;
        try
        {
            stepVersionsMap = stepVersionService.GetStepVersions(instance, instanceHistory) ?? [];
        }
        catch (StepVersionHistoryUnavailableException ex)
        {
            logger.LogWarning(ex, "Omitting attempt history for instance {InstanceId}: {Reason}", instance.Id,
                ex.Message);
            stepVersionsMap = [];
        }

        var stepVersionDtos = new Dictionary<string, StepVersionsDto>();
        foreach (var (name, versions) in stepVersionsMap)
            stepVersionDtos[name] = new StepVersionsDto(
                versions.Current == null
                    ? null
                    : await CreateStepVersionDto(versions.Current, instance, instanceHistory, displayNames, ct),
                (await Task.WhenAll(versions.History.Select(version =>
                    CreateStepVersionDto(version, instance, instanceHistory, displayNames, ct)))).ToList());
        var effectiveEventLogs = EventHistory.Project(instanceHistory.EventLogs);
        var activeSteps = modelService.GetActiveSteps(instance).ToHashSet();
        var actionSteps = actions.SelectMany(action => action.DisplaySteps ?? []).ToHashSet();
        var steps = await Task.WhenAll(workflowDefinition.Steps
            .Where(s => s.Condition.IsMet(context))
            .Select(s => CreateStepDto(s, instance, stepVersionDtos, instanceHistory, context, activeSteps,
                effectiveEventLogs, submissionDtos, actionSteps, ct)));

        var editActions = permissions.Where(a => a.Type == RoleAction.Edit).ToArray();
        var canEditByProperty = rightsService.CanEditProperties(
            instance,
            workflowDefinition.EditableRelatedUsers.Select(r => r.Property),
            editActions);
        var infoCards = visibleCards
            .Select(card => CreateInfoCard(workflowDefinition, card, context, canEditByProperty))
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
            submissionDtos,
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
    /// Creates a StepDto with versions from the map, recursively handling child steps
    /// </summary>
    private async Task<StepDto> CreateStepDto(
        Step step,
        WorkflowInstance instance,
        Dictionary<string, StepVersionsDto> stepVersionsMap,
        WorkflowInstanceHistory instanceHistory,
        ObjectContext context,
        HashSet<string> activeSteps,
        IReadOnlyList<InstanceEventLogEntry> effectiveEventLogs,
        SubmissionDto[] unversionedSubmissions,
        HashSet<string> actionSteps,
        CancellationToken ct)
    {
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var deadline = GetDeadline(step, context, activeSteps);
        var headerStatus = HasPassedDeadline(step, context, activeSteps)
            ? new StepHeaderStatusDto(StepHeaderPillType.Error, null)
            : stepProgressResolver.ResolveHeader(step, instance);

        var versionDtos = stepVersionsMap.GetValueOrDefault(step.Name);

        var owner = step;
        while (!stepVersionsMap.ContainsKey(owner.Name) && owner.ParentStep != null)
            owner = owner.ParentStep;
        var currentDto = stepVersionsMap.GetValueOrDefault(owner.Name)?.Current;
        var subtree = Descendants(step).Select(s => s.Name).ToHashSet();
        IEnumerable<SubmissionDto> assignedSubmissions =
            stepVersionsMap.Count == 0 ? unversionedSubmissions : currentDto?.Submissions ?? [];
        var displayedSubmissions = assignedSubmissions.Where(s =>
            s.Form.Step != null && subtree.Contains(s.Form.Step)).ToList();

        var children = step.Children.Length != 0
            ? await Task.WhenAll(step.Children
                .Where(s => s.Condition.IsMet(context))
                .Select(s => CreateStepDto(s, instance, stepVersionsMap, instanceHistory, context, activeSteps,
                    effectiveEventLogs, unversionedSubmissions, actionSteps, ct)))
            : null;
        // Match the step where the UI displays the submission, which can differ from the action's step.
        // For older forms without an assigned step, keep using the step's submit actions.
        // Forms without questions, such as confirmation dialogs, leave no submission content to show.
        var submissionForms = workflowDef.Forms
            .Where(form => form.Step == step.Name ||
                           form.Step == null && step.Actions.Any(action =>
                               action.Type == RoleAction.Submit && action.AllForms.Contains(form.Name)))
            .Where(form => form.ActualForm.PropertyDefinitions.Any())
            .ToArray();
        var submissionEventIds = submissionForms
            .SelectMany(FormSubmissionState.GetSubmissionEventIds)
            .ToHashSet();
        var hasSubmission = submissionForms.Any(form =>
                                FormSubmissionState.Resolve(instance, form, workflowDef).IsSubmitted) ||
                            effectiveEventLogs.Any(log =>
                                submissionEventIds.Contains(log.EventId) &&
                                log.Operation is EventLogOperation.Create or EventLogOperation.Update);
        // Hard deadlines end the submission expectation, including inherited deadlines.
        var expectsSubmission = activeSteps.Contains(step.Name) && !step.HasPassedHardDeadline(context) && step.Actions
            .Where(action => action.Type == RoleAction.Submit && action.Condition.IsMet(context))
            .SelectMany(action => action.AllForms)
            .Distinct()
            .Select(formName => modelService.GetForm(instance, formName))
            .Any(form => !FormSubmissionState.Resolve(instance, form, workflowDef).IsSubmitted);
        UndoCandidateDto? undoCandidate = null;
        if (step.ParentStep == null &&
            await undoService.GetCandidate(instance, step.Name, instanceHistory.EventLogs)
                is { OperationMetadata: { } operation } root)
        {
            var candidateStep = workflowDef.AllSteps.FirstOrDefault(s => s.Name == operation.Step);
            var form = operation.Type == OperationType.FormSubmission
                ? modelService.TryGetForm(instance, operation.Source)
                : null;
            var sourceTitle = operation.Type switch
            {
                OperationType.FormSubmission =>
                    form?.Title ?? form?.ActualForm.Title ?? form?.ActualForm.Name ?? operation.Source,
                OperationType.ExecuteAction => candidateStep?.Actions.FirstOrDefault(action =>
                    action.Type == RoleAction.Execute && action.Name == operation.Source)?.Label ?? operation.Source,
                _ => operation.Source
            };
            undoCandidate = new UndoCandidateDto(operation.Type,
                candidateStep?.DisplayTitle ?? operation.Step,
                sourceTitle,
                root.Timestamp,
                operation.Id);
        }

        var isAvailable =
            activeSteps.Contains(step.Name) || step.HasEnded(context) ||
            // Reached attempts remain inspectable while progression temporarily makes the step inactive.
            versionDtos?.Current != null || versionDtos?.History.Count > 0 ||
            // Submission history can exist even when this viewer cannot see its answers.
            displayedSubmissions.Count != 0 || hasSubmission || actionSteps.Contains(step.Name) ||
            // Expired steps must still show their deadline explanation.
            deadline is { IsPassed: true } or { Message: not null } ||
            // Grouping steps must open to expose available children.
            children?.Any(child => child.IsAvailable) == true;

        return new StepDto(
            step.Name,
            step.DisplayTitle,
            step.Icon,
            step.EndEvent,
            step.GetEndDate(instance, workflowDef),
            deadline,
            children,
            headerStatus,
            step.ResultsType,
            expectsSubmission,
            hasSubmission,
            step.HierarchyMode,
            step.ChildrenLayout,
            versionDtos,
            undoCandidate,
            displayedSubmissions,
            isAvailable
        );
    }

    private static DeadlineDto? GetDeadline(Step step, ObjectContext context, HashSet<string> activeSteps)
    {
        var deadline = step.Deadline;
        if (deadline == null)
            return null;

        var isPassed = activeSteps.Contains(step.Name) && !step.HasEnded(context) && deadline.HasPassed(context);
        var message = isPassed && deadline.Type == DeadlineType.Hard
            ? deadline.TextTemplate?.Apply(context)
            : null;

        return new DeadlineDto(step.Deadline?.Evaluate(context), deadline.Type, isPassed, message);
    }

    private static bool HasPassedDeadline(Step step, ObjectContext context, HashSet<string> activeSteps)
        => !step.HasEnded(context) && ((activeSteps.Contains(step.Name) && step.Deadline?.HasPassed(context) == true) ||
                                       step.Children.Where(child => child.Condition.IsMet(context))
                                           .Any(child => HasPassedDeadline(child, context, activeSteps)));

    private static IEnumerable<Step> Descendants(Step step)
        => new[] { step }.Concat(step.Children.SelectMany(Descendants));

    private async Task<StepVersionDto> CreateStepVersionDto(
        StepVersion stepVersion,
        WorkflowInstance instance,
        WorkflowInstanceHistory instanceHistory,
        IReadOnlyDictionary<string, string> displayNames,
        CancellationToken ct)
    {
        var historical = stepVersion.FrozenAt != null;
        var snapshot = historical
            ? workflowInstanceService.GetAsOfTimestamp(instance, stepVersion.FrozenAt!.Value,
                instanceHistory with { EventLogs = EventHistory.Project(instanceHistory.EventLogs).ToList() })
            : instance;
        var allowedViewActions = await rightsService.GetAllowedActions(instance, RoleAction.View);
        var editActions = historical ? [] : await rightsService.GetAllowedActions(instance, RoleAction.Edit);
        var workflowDef = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var submissions = new List<SubmissionDto>();
        foreach (var member in stepVersion.Submissions)
        {
            var form = modelService.GetForm(instance, member.FormName);
            if (!allowedViewActions.Any(action => action.MatchesForm(form.Name)))
                continue;
            // A reset can suppress the event while its submission still belongs to the preceding attempt.
            var submissionState = new FormSubmissionState(FormSubmissionState.GetSubmissionEventIds(form),
                member.EventIds.Select(id => new InstanceEvent { Id = id, Date = member.DateSubmitted }).ToArray(),
                member.DateSubmitted);
            var context = modelService.CreateContext(snapshot);
            await instanceService.Enrich(workflowDef, [context], form.ActualForm.Lookups, ct, replaceStep: false);
            var dto = submissionDtoFactory.Create(snapshot, form, submissionState,
                modelService.GetQuestionStatus(snapshot, form, false),
                editActions.Where(a => a.MatchesForm(form.Name)).Select(a => a.Type).ToArray(),
                historical ? null : instanceHistory, displayNames, context);
            submissions.Add(dto with { Form = dto.Form with { Step = member.Step ?? dto.Form.Step } });
        }

        return new StepVersionDto
        {
            VersionNumber = stepVersion.VersionNumber,
            CompletionTimestamp = stepVersion.CompletionTimestamp,
            Submissions = submissions
        };
    }

    private InfoCardDto? CreateInfoCard(WorkflowDefinition workflowDefinition, InfoCard card, ObjectContext context,
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
                card.TitleTemplate!.Apply(context),
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
                            workflowDefinition, relatedUser, group.AllowEditing, context, canEditByProperty))
                        .Where(role => role.Users.Length > 0 || role.AllowsAssignment)
                        .ToArray()))
                .Where(group => group.UserRoles.Length > 0)
                .ToArray();
            var items = CreateInfoCardItems(card, context);
            return groups.Length == 0 && items.Length == 0
                ? null
                : new InfoCardDto(card.Name, card.TitleTemplate!.Apply(context), type, Groups: groups, Items: items);
        }

        if (type == InfoCardType.Links)
        {
            var items = CreateInfoCardItems(card, context);
            return items.Length == 0
                ? null
                : new InfoCardDto(card.Name, card.TitleTemplate!.Apply(context), type, Items: items);
        }

        if (type == InfoCardType.Text)
        {
            return new InfoCardDto(card.Name, card.TitleTemplate!.Apply(context), type, Content: card.Content);
        }

        return new InfoCardDto(card.Name, card.TitleTemplate!.Apply(context), type);
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

    private static RelatedUserRolesDto CreateRelatedUser(WorkflowDefinition workflowDefinition,
        RelatedUser relatedUser, bool allowEditing,
        ObjectContext context, Dictionary<string, bool> canEditByProperty)
    {
        var value = context.Get(relatedUser.Property);
        var users = value is InstanceUser user ? [user] : value as InstanceUser[] ?? [];
        var genericEmail = workflowDefinition.Roles
            .GetOrDefault(relatedUser.PropertyDefinition?.Name ?? "")
            ?.GenericEmail;
        var usersDto = users.Select(instanceUser =>
        {
            var dto = UserDto.CreateFromInstanceUser(instanceUser);
            return string.IsNullOrWhiteSpace(genericEmail) ? dto : dto with { Email = genericEmail };
        }).ToArray();
        return new RelatedUserRolesDto(
            relatedUser.Property,
            relatedUser.DisplayTitle,
            usersDto,
            (relatedUser.PropertyDefinition as UserPropertyDefinition)?.AllowsExternalUsers ?? false,
            !relatedUser.PropertyDefinition?.IsRequired ?? false,
            relatedUser.PropertyDefinition?.IsArray ?? false,
            allowEditing && canEditByProperty.GetValueOrDefault(relatedUser.Property));
    }
}