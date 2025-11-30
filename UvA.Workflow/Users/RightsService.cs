using System.Security.Claims;
using UvA.Workflow.Infrastructure;
using Domain_Action = UvA.Workflow.Entities.Domain.Action;

namespace UvA.Workflow.Users;

public class RightsService(
    ModelService modelService,
    IUserService userService,
    IWorkflowInstanceRepository workflowInstanceRepository)
{
    public async Task<IEnumerable<string>> GetGlobalRoles() =>
        (await userService.GetRolesOfCurrentUser()).ToList()
        .Append("Registered");


    public async Task<Domain_Action[]> GetAllowedActions(string? entityType, params RoleAction[] actions)
        => (await GetGlobalRoles())
            .Select(r => modelService.Roles.GetValueOrDefault(r))
            .Where(r => r != null)
            .SelectMany(r => r!.Actions
                .Where(a => (a.Condition == null || a.Condition.IsMet(new ObjectContext(new())))
                            && actions.Contains(a.Type)
                            && (a.EntityType == null || a.EntityType == entityType?.Split('/')[0])
                ))
            .ToArray();

    private async Task<Role?[]> GetInstanceRoles(WorkflowInstance instance, CancellationToken ct = default)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null) return [];

        // Process inherited roles
        var properties = modelService.EntityTypes[instance.EntityType].Properties.Values;

        var inheritedRoles = properties
            .Where(p => p.InheritRoles.Any())
            .SelectMany(p => p.InheritRoles.Select(r => new
            {
                Role = r,
                EntityType = p.EntityType,
                InstanceId = instance.Properties.GetValueOrDefault(p.Name)?.ToString()
            }))
            .Where(r => r.EntityType != null && !string.IsNullOrEmpty(r.InstanceId))
            .ToList();

        var inheritedInstances = inheritedRoles.Any()
            ? (await workflowInstanceRepository.GetAllById(
                inheritedRoles.Select(r => r.InstanceId!).Distinct().ToArray(),
                inheritedRoles.Select(r => new { r.Role, Key = r.EntityType!.GetKey(r.Role) }).Distinct()
                    .ToDictionary(r => r.Role, r => r.Key),
                ct
            )).ToDictionary(r => r["_id"].ToString()!)
            : new();

        var inheritedProperties = inheritedRoles.Select(r => new
        {
            Name = r.Role,
            Value = inheritedInstances.GetValueOrDefault(r.InstanceId!)?.GetValueOrDefault(r.Role)
        });

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
            .ToArray();
    }

    public async Task<Domain_Action[]> GetAllowedActions(WorkflowInstance instance, params RoleAction[] actions)
    {
        var globalUserRoles = await GetGlobalRoles();
        var globalRoles = globalUserRoles.Select(gur => modelService.Roles.GetValueOrDefault(gur)).Where(r => r != null)
            .ToArray();
        var instanceRoles = await GetInstanceRoles(instance);

        return globalRoles
            .Concat(instanceRoles)
            .SelectMany(r => r!.Actions
                .Where(a => (a.Condition == null || a.Condition.IsMet(modelService.CreateContext(instance)))
                            && actions.Contains(a.Type)
                            && (a.Steps.Length == 0 || a.Steps.Intersect(modelService.GetActiveSteps(instance)).Any())
                            && (a.EntityType == null || a.EntityType == instance.EntityType)
                ))
            .Distinct()
            .ToArray();
    }

    public async Task<bool> CanAny(string? entityType, params RoleAction[] actions)
        => (await GetAllowedActions(entityType, actions)).Any();

    public async Task<Domain_Action[]> GetAllowedFormActions(WorkflowInstance instance, string form,
        params RoleAction[] actions)
    {
        var allowed = await GetAllowedActions(instance, actions);
        return allowed.Where(f => f.MatchesForm(form)).ToArray();
    }

    public async Task<bool> Can(WorkflowInstance instance, RoleAction action, string? form = null)
    {
        var actions = await GetAllowedActions(instance, action);
        return actions.Any(f => form == null || f.MatchesForm(form));
    }

    public async Task EnsureAuthorizedForAction(WorkflowInstance instance, RoleAction action, string? form = null)
    {
        if (!await Can(instance, action, form))
            throw new ForbiddenWorkflowActionException(instance.Id, action, form);
    }

    public async Task<bool> CanViewCollection(WorkflowInstance instance, string collection)
    {
        var actions = await GetAllowedActions(instance, RoleAction.View);
        return actions.Any(f => f.MatchesCollection(collection));
    }
}