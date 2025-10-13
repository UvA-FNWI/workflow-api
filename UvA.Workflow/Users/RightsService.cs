using System.Security.Claims;
using Domain_Action = UvA.Workflow.Entities.Domain.Action;

namespace UvA.Workflow.Services;

public class RightsService(
    ModelService modelService,
    IUserService userService,
    UserCacheService userCacheService)
{
    private readonly ClaimsPrincipal _principal = new(); // Mock for now

    public async Task<GlobalRole[]> GetGlobalRoles() =>
        (await userService.GetRoles(_principal))
        .Append(new GlobalRole("Registered"))
        .ToArray();

    public async Task<User?> GetUser(CancellationToken ct = default)
    {
        var extUser = userService.GetUserInfo(_principal);
        if (extUser == null)
            return null;
        return await userCacheService.GetUser(extUser, ct);
    }

    public async Task<string?> GetUserId()
    {
        var user = await GetUser();
        return user?.Id;
    }

    public async Task<Domain_Action[]> GetAllowedActions(string? entityType, params RoleAction[] actions)
        => (await GetGlobalRoles())
            .Select(r => modelService.Roles.GetValueOrDefault(r.RoleName))
            .Where(r => r != null)
            .SelectMany(r => r!.Actions
                .Where(a => (a.Condition == null || a.Condition.IsMet(new ObjectContext(new())))
                            && actions.Contains(a.Type)
                            && (a.EntityType == null || a.EntityType == entityType?.Split('/')[0])
                ))
            .ToArray();

    private async Task<Role?[]> GetInstanceRoles(WorkflowInstance instance)
    {
        var userId = await GetUserId();
        if (userId == null) return [];

        return modelService.EntityTypes[instance.EntityType].Properties.Values
            .Where(p => p.DataType == DataType.User)
            .Select(p => new { p.Name, Value = instance.Properties.GetValueOrDefault(p.Name) })
            .Where(p => p.Value != null)
            .Where(p => p.Value switch
            {
                BsonDocument d => BsonSerializer.Deserialize<User>(d).Id == userId,
                BsonArray a => a.Any(v =>
                    v is BsonDocument d && BsonSerializer.Deserialize<User>(d).Id == userId),
                _ => false
            })
            .Select(p => modelService.Roles.GetValueOrDefault(p.Name))
            .ToArray();
    }

    public async Task<Domain_Action[]> GetAllowedActions(WorkflowInstance instance, params RoleAction[] actions)
        => (await GetGlobalRoles())
            .Select(r => modelService.Roles.GetValueOrDefault(r.RoleName))
            .Concat(await GetInstanceRoles(instance))
            .Where(r => r != null)
            .SelectMany(r => r!.Actions
                .Where(a => (a.Condition == null || a.Condition.IsMet(modelService.CreateContext(instance)))
                            && actions.Contains(a.Type)
                            && (a.Steps.Length == 0 || a.Steps.Intersect(modelService.GetActiveSteps(instance)).Any())
                            && (a.EntityType == null || a.EntityType == instance.EntityType)
                ))
            .Distinct()
            .ToArray();

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

    public async Task<bool> CanViewCollection(WorkflowInstance instance, string collection)
    {
        var actions = await GetAllowedActions(instance, RoleAction.View);
        return actions.Any(f => f.MatchesCollection(collection));
    }
}