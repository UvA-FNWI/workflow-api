using UvA.Workflow.Infrastructure;
using UvA.Workflow.WorkflowModel.Conditions;
using Domain_Action = UvA.Workflow.Entities.Domain.Action;

namespace UvA.Workflow.Users;

public enum RightsEvaluationMode
{
    RequestContext,
    RealUser
}

public record WorkflowImpersonationRole(string Name, BilingualString Title);

public class RightsService(
    ModelService modelService,
    IUserService userService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    IImpersonationContextService? impersonationContextService = null)
{
    private readonly IImpersonationContextService impersonationContextService =
        impersonationContextService ?? new NoImpersonationContextService();

    public async Task<IEnumerable<string>> GetGlobalRoles() =>
        (await userService.GetRolesOfCurrentUser()).ToList()
        .Append("Registered");

    public WorkflowImpersonationRole[] GetWorkflowRelevantRoles(string workflowDefinition)
    {
        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return [];

        var actionRoles = modelService.Roles.Values
            .Where(r => r.Actions.Any(a => a.WorkflowDefinition == null || a.WorkflowDefinition == workflowDefinition))
            .Select(r => r.Name);

        var definitionRoles = definition.Properties
            .Where(p => p.DataType == DataType.User)
            .Select(p => p.Name)
            .Concat(definition.Properties.SelectMany(p => p.InheritedRoles));

        return actionRoles
            .Concat(definitionRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(r => modelService.Roles.GetValueOrDefault(r))
            .Where(r => r != null)
            .Select(r => new WorkflowImpersonationRole(r!.Name, r.DisplayTitle))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public WorkflowImpersonationRole? NormalizeWorkflowRelevantRole(string workflowDefinition, string roleName)
        => GetWorkflowRelevantRoles(workflowDefinition)
            .FirstOrDefault(r => string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase));


    public async Task<Domain_Action[]> GetAllowedActions(string? workflowDefinition, params RoleAction[] actions)
        => (await GetGlobalRoles())
            .Select(r => modelService.Roles.GetValueOrDefault(r))
            .Where(r => r != null)
            .SelectMany(r => r!.Actions
                .Where(a => (a.Condition == null || a.Condition.IsMet(new ObjectContext(new())))
                            && actions.Contains(a.Type)
                            && (a.WorkflowDefinition == null ||
                                a.WorkflowDefinition == workflowDefinition?.Split('/')[0])
                ))
            .ToArray();

    private async Task<Role?[]> GetInstanceRoles(WorkflowInstance instance, CancellationToken ct = default)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null) return [];

        // Process inherited roles
        var properties = modelService.WorkflowDefinitions[instance.WorkflowDefinition].Properties;

        var inheritedRoles = properties
            .Where(p => p.InheritedRoles.Any())
            .SelectMany(p => p.InheritedRoles.Select(r => new
            {
                Role = r,
                p.WorkflowDefinition,
                InstanceIds = instance.Properties.GetValueOrDefault(p.Name) switch
                {
                    BsonArray a => a.Select(v => v.AsString).ToArray(),
                    BsonString s => [s.AsString],
                    _ => []
                }
            }))
            .Where(r => r.WorkflowDefinition != null && r.InstanceIds.Length > 0)
            .ToList();

        var inheritedViaInstances = inheritedRoles.Any()
            ? (await workflowInstanceRepository.GetAllById(
                inheritedRoles.SelectMany(r => r.InstanceIds).Distinct().ToArray(),
                inheritedRoles.Select(r => new { r.Role, Key = r.WorkflowDefinition!.GetKey(r.Role) }).Distinct()
                    .ToDictionary(r => r.Role, r => r.Key),
                ct
            )).ToDictionary(r => r["_id"].ToString()!)
            : new();

        var inheritedProperties = inheritedRoles.SelectMany(r => r.InstanceIds.Select(i => new
        {
            Name = r.Role,
            Value = inheritedViaInstances.GetValueOrDefault(i!)?.GetValueOrDefault(r.Role)
        }));

        return properties
            .Where(p => p.DataType == DataType.User)
            .Select(p => new { p.Name, Value = instance.Properties.GetValueOrDefault(p.Name) })
            .Concat(inheritedProperties)
            .Where(p => p.Value != null)
            .Where(p => p.Value switch
            {
                BsonDocument d => BsonSerializer.Deserialize<User>(d).Id == user.Id,
                BsonArray a => a.Any(v =>
                    v is BsonDocument d && BsonSerializer.Deserialize<User>(d).Id == user.Id),
                _ => false
            })
            .Select(p => modelService.Roles.GetValueOrDefault(p.Name))
            .Where(p => p != null)
            .ToArray();
    }

    private Domain_Action[] GetAllowedActions(WorkflowInstance instance, IEnumerable<Role?> roles,
        params RoleAction[] actions)
    {
        var context = modelService.CreateContext(instance);
        var activeSteps = modelService.GetActiveSteps(instance);

        return roles
            .Where(r => r != null)
            .SelectMany(r => r!.Actions
                .Where(a => (a.Condition == null || a.Condition.IsMet(context))
                            && actions.Contains(a.Type)
                            && (a.Steps.Length == 0 || a.Steps.Intersect(activeSteps).Any())
                            && (a.WorkflowDefinition == null || a.WorkflowDefinition == instance.WorkflowDefinition)
                ))
            .Distinct()
            .ToArray();
    }

    private async Task<Domain_Action[]> GetAllowedActionsForRealUser(WorkflowInstance instance,
        params RoleAction[] actions)
    {
        var globalUserRoles = await GetGlobalRoles();
        var globalRoles = globalUserRoles.Select(gur => modelService.Roles.GetValueOrDefault(gur))
            .Where(r => r != null)
            .ToArray();
        var instanceRoles = await GetInstanceRoles(instance);

        return GetAllowedActions(instance, globalRoles.Concat(instanceRoles), actions);
    }

    private async Task<Domain_Action[]> GetAllowedActionsForRequestContext(
        WorkflowInstance instance,
        params RoleAction[] actions)
    {
        var impersonatedRoleName = await impersonationContextService.GetImpersonatedRole(instance);
        if (string.IsNullOrWhiteSpace(impersonatedRoleName))
            return await GetAllowedActionsForRealUser(instance, actions);

        var normalizedRoleName = NormalizeWorkflowRelevantRole(instance.WorkflowDefinition, impersonatedRoleName);
        if (normalizedRoleName == null) return [];

        var role = modelService.Roles.GetValueOrDefault(normalizedRoleName.Name);
        if (role == null) return [];
        return GetAllowedActions(instance, [role], actions);
    }

    public Task<Domain_Action[]> GetAllowedActions(WorkflowInstance instance, params RoleAction[] actions)
        => GetAllowedActions(instance, RightsEvaluationMode.RequestContext, actions);

    public async Task<Domain_Action[]> GetAllowedActions(
        WorkflowInstance instance,
        RightsEvaluationMode evaluationMode,
        params RoleAction[] actions)
        => evaluationMode switch
        {
            RightsEvaluationMode.RealUser => await GetAllowedActionsForRealUser(instance, actions),
            _ => await GetAllowedActionsForRequestContext(instance, actions)
        };

    public async Task<bool> CanAny(string? workflowDefinition, params RoleAction[] actions)
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
        => Can(instance, action, RightsEvaluationMode.RequestContext, form);

    public async Task<bool> Can(
        WorkflowInstance instance,
        RoleAction action,
        RightsEvaluationMode evaluationMode,
        string? form = null)
    {
        var actions = await GetAllowedActions(instance, evaluationMode, action);
        return actions.Any(f => form == null || f.MatchesForm(form));
    }

    public Task EnsureAuthorizedForAction(WorkflowInstance instance, RoleAction action, string? form = null)
        => EnsureAuthorizedForAction(instance, action, RightsEvaluationMode.RequestContext, form);

    public async Task EnsureAuthorizedForAction(
        WorkflowInstance instance,
        RoleAction action,
        RightsEvaluationMode evaluationMode,
        string? form = null)
    {
        if (!await Can(instance, action, evaluationMode, form))
            throw new ForbiddenWorkflowActionException(instance.Id, action, form);
    }

    public Task<bool> CanViewCollection(WorkflowInstance instance, string collection)
        => CanViewCollection(instance, collection, RightsEvaluationMode.RequestContext);

    public async Task<bool> CanViewCollection(
        WorkflowInstance instance,
        string collection,
        RightsEvaluationMode evaluationMode)
    {
        var actions = await GetAllowedActions(instance, evaluationMode, RoleAction.View);
        return actions.Any(f => f.MatchesCollection(collection));
    }
}