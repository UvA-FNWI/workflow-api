using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Screens;

public class InstanceAuthorizationFilterService(
    RightsService rightsService,
    ModelService modelService,
    IUserService userService,
    IWorkflowInstanceRepository workflowInstanceRepository)
{
    /// <summary>
    /// Returns a MongoDB filter for instances the user can view, or null if user has global access.
    /// </summary>
    public async Task<BsonDocument?> BuildAuthorizationFilter(string workflowDefinition, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null)
        {
            // Unauthenticated user, return filter that matches nothing
            return new BsonDocument("_id", new BsonDocument("$in", new BsonArray()));
        }

        // Get all actions that allow viewing this workflow definition
        var allowedActions = await rightsService.GetAllowedActions(workflowDefinition, RoleAction.View);

        // Check if any action grants unconditional access (no condition, no step restrictions)
        var hasUnconditionalAccess = allowedActions.Any(a =>
            a.Condition == null &&
            a.Steps.Length == 0 &&
            (a.WorkflowDefinition == null || a.WorkflowDefinition == workflowDefinition));

        if (hasUnconditionalAccess)
            return null;

        var filters = new List<BsonDocument>();
        filters.AddRange(await BuildInstanceRoleFilters(workflowDefinition, user));
        filters.AddRange(await BuildInheritedRoleFilters(workflowDefinition, user, ct));

        return filters.Count switch
        {
            0 => new BsonDocument("_id", new BsonDocument("$in", new BsonArray())),
            1 => filters[0],
            _ => new BsonDocument("$or", new BsonArray(filters))
        };
    }

    /// <summary>
    /// Whether the current user can see at least one instance of the given workflow definition
    /// (either unconditional view access, or at least one instance they are authorized to view).
    /// </summary>
    public async Task<bool> HasVisibleInstances(string workflowDefinition, CancellationToken ct)
    {
        var authorizationFilter = await BuildAuthorizationFilter(workflowDefinition, ct);

        // A null filter means the user has unconditional view access (sees all instances).
        if (authorizationFilter == null)
            return true;

        var rows = await workflowInstanceRepository.GetAllByType(
            workflowDefinition,
            new Dictionary<string, string> { ["_id"] = "$_id" },
            authorizationFilter,
            ct);

        return rows.Count > 0;
    }

    private async Task<List<BsonDocument>> BuildInstanceRoleFilters(
        string workflowDefinition,
        User user)
    {
        var filters = new List<BsonDocument>();

        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return filters;

        var rolesWithViewAccess = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var globalRoles = await rightsService.GetGlobalRoles();
        foreach (var globalRoleName in globalRoles)
        {
            var role = modelService.Roles.GetValueOrDefault(globalRoleName);
            if (role != null && role.Actions.Any(a =>
                    a.Type == RoleAction.View &&
                    (a.WorkflowDefinition == null || a.WorkflowDefinition == workflowDefinition)))
            {
                rolesWithViewAccess.Add(role.Name);
            }
        }

        rolesWithViewAccess.UnionWith(
            definition.GlobalActions
                .Where(a => a.Type == RoleAction.View)
                .SelectMany(a => a.Roles)
        );

        var userId = new ObjectId(user.Id);

        filters.AddRange(
            modelService.RoleBindings.GetBindings(workflowDefinition)
                .Where(binding => binding.Source == RoleBindingSource.Direct)
                .Where(binding => rolesWithViewAccess.Contains(binding.Role))
                .Select(binding => new BsonDocument(binding.UserIdPath!, userId))
        );

        return filters;
    }

    private async Task<List<BsonDocument>> BuildInheritedRoleFilters(
        string workflowDefinition,
        User user,
        CancellationToken ct)
    {
        var filters = new List<BsonDocument>();

        var inheritedBindings = modelService.RoleBindings.GetBindings(workflowDefinition)
            .Where(binding => binding.Source == RoleBindingSource.Inherited)
            .ToList();

        if (inheritedBindings.Count == 0)
            return filters;

        var userId = new ObjectId(user.Id);

        foreach (var binding in inheritedBindings)
        {
            var referencedFilter = new BsonDocument(binding.ReferencedUserIdPath!, userId);

            // Query to get IDs of referenced instances where user has the role
            var referencedInstanceIds = await workflowInstanceRepository.GetAllByType(
                binding.ReferencedWorkflowDefinition!,
                new Dictionary<string, string> { ["_id"] = "$_id" },
                referencedFilter,
                ct);

            var matchingIds = referencedInstanceIds
                .Select(r => r["_id"].AsObjectId)
                .ToList();

            if (matchingIds.Any())
            {
                filters.Add(new BsonDocument(binding.PropertyPath,
                    new BsonDocument("$in", new BsonArray(matchingIds.Select(id => id.ToString())))));
            }
        }

        return filters;
    }
}