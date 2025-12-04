using System.Security.Claims;
using UvA.Workflow.Infrastructure;
using Domain_Action = UvA.Workflow.Entities.Domain.Action;

namespace UvA.Workflow.Users;

public class RightsService(
    ModelService modelService,
    IUserService userService)
{
    public async Task<IEnumerable<string>> GetGlobalRoles() =>
        (await userService.GetRolesOfCurrentUser()).ToList()
        .Append("Registered");


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

    private async Task<Role?[]> GetInstanceRoles(WorkflowInstance instance)
    {
        var user = await userService.GetCurrentUser();
        if (user == null) return [];

        return modelService.WorkflowDefinitions[instance.WorkflowDefinition].Properties
            .Where(p => p.DataType == DataType.User)
            .Select(p => new { p.Name, Value = instance.Properties.GetValueOrDefault(p.Name) })
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
                            && (a.WorkflowDefinition == null || a.WorkflowDefinition == instance.WorkflowDefinition)
                ))
            .Distinct()
            .ToArray();
    }

    public async Task<bool> CanAny(string? workflowDefinition, params RoleAction[] actions)
        => (await GetAllowedActions(workflowDefinition, actions)).Any();

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