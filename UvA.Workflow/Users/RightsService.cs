using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel;
using UvA.Workflow.WorkflowModel.Conditions;
using Domain_Action = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Users;

public enum RightsEvaluationMode
{
    /// <summary>Applies the role-impersonation header, if present.</summary>
    RequestContext,

    /// <summary>The user's own roles, ignoring role impersonation. User impersonation still applies.</summary>
    RealUser
}

public record WorkflowImpersonationRole(string Name, BilingualString Title);

public record RoleAllowedActions(WorkflowImpersonationRole Role, Domain_Action[] Actions);

public class RightsService(
    ModelService modelService,
    IUserService userService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    IImpersonationContextService? impersonationContextService = null)
{
    private readonly IImpersonationContextService _impersonationContextService =
        impersonationContextService ?? new NoImpersonationContextService();

    /// <summary>
    /// Returns the roles of the current user, plus "Registered" which is the default role for all users.
    /// </summary>
    public async Task<IEnumerable<string>> GetGlobalUserRoles() =>
        (await userService.GetRolesOfCurrentUser()).ToList()
        .Append("Registered");

    private WorkflowImpersonationRole[] GetImpersonationCandidateRoles(string workflowDefinition)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return [];

        var roles = definition.Roles;

        var actionRoles = roles
            .Where(r => r.Actions.Count > 0).Select(r => r.Name);

        var definitionRoles = definition.Properties
            .Where(p => p.DataType == DataType.User)
            .Select(p => p.Name);

        return actionRoles
            .Concat(definitionRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(r => roles.GetOrDefault(r))
            .Where(r => r != null)
            .Select(r => new WorkflowImpersonationRole(r!.Name, r.DisplayTitle))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // View rights keep a role eligible for impersonation even after a hard deadline.
    public WorkflowImpersonationRole[] GetImpersonationTargetRoles(WorkflowInstance instance)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        return GetImpersonationCandidateRoles(instance.WorkflowDefinition)
            .Where(r => definition.Roles.GetOrDefault(r.Name) is { } role
                        && GetAllowedActions(instance, [role], RoleAction.View).Any())
            .ToArray();
    }

    public WorkflowImpersonationRole? NormalizeImpersonationTargetRole(WorkflowInstance instance, string roleName)
        => GetImpersonationTargetRoles(instance)
            .FirstOrDefault(r => string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase));

    public RoleAllowedActions[] GetAllowedActionsPerTargetRole(WorkflowInstance instance,
        params RoleAction[] actions)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var context = modelService.CreateContext(instance);
        var activeSteps = modelService.GetActiveSteps(instance);

        return GetImpersonationTargetRoles(instance)
            .Select(r =>
            {
                var roleActions = GetAllowedActions(instance, context, activeSteps,
                    [definition.Roles.GetOrDefault(r.Name)], actions);
                return new RoleAllowedActions(r, GetAllowedActions(instance, context, roleActions));
            })
            .ToArray();
    }


    public async Task<Domain_Action[]> GetAllowedActions(string workflowDefinition, params RoleAction[] actions)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return [];

        return (await GetGlobalUserRoles())
            .Select(r => definition.Roles.GetOrDefault(r))
            .Where(r => r != null)
            .SelectMany(r => r!.Actions
                .Where(a => (a.Condition == null || a.Condition.IsMet(new ObjectContext(new())))
                            && actions.Contains(a.Type)
                            && (a.WorkflowDefinition == null ||
                                a.WorkflowDefinition == workflowDefinition.Split('/')[0])
                ))
            .ToArray();
    }

    public async Task<string[]> GetViewerRoles(WorkflowInstance instance, CancellationToken ct = default)
    {
        var impersonatedRoleName = await _impersonationContextService.GetImpersonatedRole(instance, ct);
        if (!string.IsNullOrWhiteSpace(impersonatedRoleName))
        {
            var normalized = NormalizeImpersonationTargetRole(instance, impersonatedRoleName);
            return normalized != null ? [normalized.Name] : [];
        }

        var globalUserRoles = await GetGlobalUserRoles();
        var instanceRoles = await GetInstanceRoles(instance, ct);

        return globalUserRoles
            .Concat(instanceRoles.Where(r => r != null).Select(r => r!.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<Role?[]> GetInstanceRoles(WorkflowInstance instance, CancellationToken ct = default)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null) return [];

        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        var inheritedProperties = await GetInheritedRoles(definition, instance, ct);

        return definition.Properties
            .Where(p => p.DataType == DataType.User)
            .Select(p => new RoleValue(p.Name, instance.GetProperty(p.Name)))
            .Concat(inheritedProperties)
            .Where(p => p.Value != null)
            .Where(p => p.Value switch
            {
                BsonDocument d => BsonSerializer.Deserialize<InstanceUser>(d).Id == user.Id,
                BsonArray a => a.Any(v =>
                    v is BsonDocument d && BsonSerializer.Deserialize<InstanceUser>(d).Id == user.Id),
                _ => false
            })
            .Select(p => definition.Roles.GetOrDefault(p.Name))
            .Where(p => p != null)
            .ToArray();
    }

    private sealed record RoleValue(string Name, BsonValue? Value);

    /// <summary>
    /// Resolves inheritable user-role assignments from referenced instances.
    /// Permissions remain defined by the consuming workflow.
    /// </summary>
    private Task<IEnumerable<RoleValue>> GetInheritedRoles(
        WorkflowDefinition definition,
        WorkflowInstance instance,
        CancellationToken ct = default)
        => ResolveInheritableRoles(
            definition, instance, new HashSet<string>(), false, ct);

    private async Task<IEnumerable<RoleValue>> ResolveInheritableRoles(
        WorkflowDefinition definition,
        WorkflowInstance instance,
        HashSet<string> visited,
        bool includeOwnRoles,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // A reference cycle must not cause infinite role resolution.
        if (!visited.Add(instance.Id))
            return [];

        var roles = new List<RoleValue>();

        if (includeOwnRoles)
        {
            roles.AddRange(
                definition.Properties.OfType<UserPropertyDefinition>()
                    .Where(p => p.IsInheritableRole == true)
                    .Select(p => new RoleValue(
                        p.Name, instance.GetProperty(p.Name))));
        }

        var references = definition.Properties.OfType<ReferencePropertyDefinition>()
            .Where(p => p.WorkflowDefinition != null)
            .SelectMany(p =>
            {
                var ids = instance.GetProperty(p.Name) switch
                {
                    BsonArray a => a.Select(v => v.AsString).ToArray(),
                    BsonString s => [s.AsString],
                    _ => Array.Empty<string>()
                };

                return ids.Select(id => (
                    Id: id,
                    Definition: p.WorkflowDefinition!));
            })
            .Distinct()
            .ToArray();

        if (references.Length == 0)
            return roles;

        var referencedInstances = await workflowInstanceRepository.GetByIds(
            references.Select(r => r.Id).Distinct().ToArray(), ct);

        foreach (var referencedInstance in referencedInstances)
        {
            var reference = references.FirstOrDefault(r =>
                r.Id == referencedInstance.Id
                && r.Definition.Name == referencedInstance.WorkflowDefinition);

            // Only follow instances of the reference property's declared type.
            if (reference.Definition == null)
                continue;

            roles.AddRange(await ResolveInheritableRoles(
                reference.Definition,
                referencedInstance,
                visited,
                true,
                ct));
        }

        return roles;
    }

    private Domain_Action[] GetAllowedActions(WorkflowInstance instance, IEnumerable<Role?> roles,
        params RoleAction[] actions)
        => GetAllowedActions(instance, modelService.CreateContext(instance),
            modelService.GetActiveSteps(instance), roles, actions);

    private Domain_Action[] GetAllowedActions(WorkflowInstance instance, ObjectContext context,
        string[] activeSteps, IEnumerable<Role?> roles, params RoleAction[] actions)
    {
        return roles
            .Where(r => r != null)
            .SelectMany(r => r!.Actions
                .Where(a => (a.Condition == null || a.Condition.IsMet(context))
                            && actions.Contains(a.Type)
                            && (a.Steps.Length == 0 || modelService
                                .GetAvailableStepsForAction(instance, a, activeSteps, context).Length > 0)
                            && (a.WorkflowDefinition == null || a.WorkflowDefinition == instance.WorkflowDefinition)
                ))
            .Distinct()
            .ToArray();
    }

    private async Task<Domain_Action[]> GetAllowedActionsForRealUser(WorkflowInstance instance,
        params RoleAction[] actions)
    {
        return GetAllowedActions(instance, await GetRealUserRoles(instance), actions);
    }

    private async Task<Domain_Action[]> GetAllowedActionsForRequestContext(
        WorkflowInstance instance,
        params RoleAction[] actions)
    {
        return GetAllowedActions(instance, await GetRequestContextRoles(instance), actions);
    }

    public async Task<Domain_Action[]> GetAllowedActionsForStep(
        WorkflowInstance instance,
        string stepName,
        params RoleAction[] actions)
    {
        var context = modelService.CreateContext(instance);
        var roles = await GetRequestContextRoles(instance);
        return GetAllowedActions(instance, context, [stepName], roles, actions);
    }

    private async Task<Role?[]> GetRealUserRoles(WorkflowInstance instance)
        => (await GetGlobalUserRoles())
            .Select(role => modelService.WorkflowDefinitions[instance.WorkflowDefinition].Roles.GetOrDefault(role))
            .Concat(await GetInstanceRoles(instance))
            .ToArray();

    private async Task<Role?[]> GetRequestContextRoles(WorkflowInstance instance)
    {
        var impersonatedRoleName = await _impersonationContextService.GetImpersonatedRole(instance);
        if (string.IsNullOrWhiteSpace(impersonatedRoleName))
            return await GetRealUserRoles(instance);

        var normalizedRoleName = NormalizeImpersonationTargetRole(instance, impersonatedRoleName);
        return normalizedRoleName == null
            ? []
            :
            [
                modelService.WorkflowDefinitions[instance.WorkflowDefinition].Roles
                    .GetOrDefault(normalizedRoleName.Name)
            ];
    }

    public async Task<Domain_Action[]> GetAllowedActionsForForm(WorkflowInstance instance, Form form,
        params RoleAction[] actions)
    {
        var allActions = await GetAllowedActions(instance, RightsEvaluationMode.RequestContext, actions);
        return allActions.Where(p => p.MatchesForm(form.Name)).ToArray();
    }

    public Task<Domain_Action[]> GetAllowedActions(WorkflowInstance instance, params RoleAction[] actions)
        => GetAllowedActions(instance, RightsEvaluationMode.RequestContext, actions);

    public async Task<Domain_Action[]> GetAllowedActions(
        WorkflowInstance instance,
        RightsEvaluationMode evaluationMode,
        params RoleAction[] actions)
    {
        var roleActions = evaluationMode switch
        {
            RightsEvaluationMode.RealUser => await GetAllowedActionsForRealUser(instance, actions),
            _ => await GetAllowedActionsForRequestContext(instance, actions)
        };
        return GetAllowedActions(instance, modelService.CreateContext(instance), roleActions);
    }

    private Domain_Action[] GetAllowedActions(WorkflowInstance instance, ObjectContext context,
        Domain_Action[] actions)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var activeSteps = modelService.GetActiveSteps(instance);
        // Check deadlines after resolving persistent steps so parent deadlines also apply to completed children.
        return actions.Where(a => a.Type == RoleAction.View || a.Steps.Length == 0 || modelService
            .GetAvailableStepsForAction(instance, a, activeSteps, context)
            .Any(name => !definition.AllSteps.Get(name).HasPassedHardDeadline(context))).ToArray();
    }

    public async Task<bool> CanAny(string workflowDefinition, params RoleAction[] actions)
        => (await GetAllowedActions(workflowDefinition, actions)).Any();

    public Task<Domain_Action[]> GetAllowedFormActions(
        WorkflowInstance instance,
        string form,
        params RoleAction[] actions)
        => GetAllowedFormActions(instance, form, RightsEvaluationMode.RequestContext, actions);

    public async Task<Domain_Action[]> GetAllowedFormActions(
        WorkflowInstance instance,
        string form,
        RightsEvaluationMode evaluationMode,
        params RoleAction[] actions)
    {
        var allowed = await GetAllowedActions(instance, evaluationMode, actions);
        return allowed.Where(f => f.MatchesForm(form)).ToArray();
    }

    public Task<bool> Can(WorkflowInstance instance, RoleAction action, string? form = null)
        => Can(instance, [action], RightsEvaluationMode.RequestContext, form);

    public async Task<bool> Can(
        WorkflowInstance instance,
        RoleAction[] actions,
        RightsEvaluationMode evaluationMode,
        string? form = null)
    {
        var allowedActions = await GetAllowedActions(instance, evaluationMode, actions);
        return allowedActions.Any(f => form == null || f.MatchesForm(form));
    }

    private async Task<bool> Can(RoleAction action)
    {
        var globalUserRoles = (await GetGlobalUserRoles())
            .Select(gur => modelService.Roles.GetValueOrDefault(gur))
            .ToArray();
        return globalUserRoles.Any(r => r != null && r.Actions.Any(a => a.Type == action));
    }

    public Task EnsureAuthorizedForAction(WorkflowInstance instance, RoleAction action, string? form = null)
        => EnsureAuthorizedForAction(instance, [action], RightsEvaluationMode.RequestContext, form);

    public Task EnsureAuthorizedForAction(WorkflowInstance instance, RoleAction[] actions)
        => EnsureAuthorizedForAction(instance, actions, RightsEvaluationMode.RequestContext);

    public async Task EnsureAuthorizedForAction(
        WorkflowInstance instance,
        RoleAction[] actions,
        RightsEvaluationMode evaluationMode,
        string? form = null)
    {
        if (!await Can(instance, actions, evaluationMode, form))
            throw new ForbiddenWorkflowActionException(instance.Id, actions[0], form);
    }

    public async Task EnsureAuthorizedForAction(RoleAction action)
    {
        if (!await Can(action))
            throw new UnauthorizedAccessException();
    }

    public async Task<bool> CanEditProperty(WorkflowInstance instance, string propertyName)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];
        var parts = propertyName.Split('.');
        if (parts.Length > 1 && definition.Properties.GetOrDefault(parts[0])?.DataType != DataType.Object)
            return false;

        var realUserEditActions =
            await GetAllowedActions(instance, RightsEvaluationMode.RealUser, RoleAction.Edit);
        if (realUserEditActions.Any(a => a.AllForms.Length == 0 && a.PropertyDefinition == null))
            return true;

        var allowedEditActions =
            await GetAllowedActions(instance, RightsEvaluationMode.RequestContext, RoleAction.Edit);
        return CanEditProperties(instance, [propertyName], allowedEditActions)[propertyName];
    }

    public Dictionary<string, bool> CanEditProperties(WorkflowInstance instance,
        IEnumerable<string> propertyNames, Domain_Action[] allowedEditActions)
    {
        var definition = modelService.WorkflowDefinitions[instance.WorkflowDefinition];

        return propertyNames.ToDictionary(
            propertyName => propertyName,
            propertyName =>
            {
                var propertyDefinition = definition.Properties.GetOrDefault(propertyName);
                if (propertyDefinition == null)
                    return false; // Property does not exist in the workflow definition (e.g. a reference)

                if (allowedEditActions.Any(a => a.AllForms.Length == 0 && a.PropertyDefinition == null))
                    return true;

                var formsContainingProperty = definition.Forms
                    .Where(f => f.PropertyDefinitions.Any(p => p.Name == propertyName))
                    .Select(f => f.Name).ToArray();

                if (allowedEditActions.Any(a => formsContainingProperty.Any(f => a.MatchesForm(f))))
                    return true;

                return allowedEditActions.Any(a => a.PropertyDefinition == propertyName);
            });
    }
}