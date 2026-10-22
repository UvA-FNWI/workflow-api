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
    public async Task<BsonDocument?> BuildAuthorizationFilter(string workflowDefinition, RoleAction action,
        CancellationToken ct, bool includeDirectRoles = true)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null)
        {
            // Unauthenticated user, return filter that matches nothing
            return new BsonDocument("_id", new BsonDocument("$in", new BsonArray()));
        }

        // Get all actions that allow viewing this workflow definition
        var allowedActions = await rightsService.GetAllowedActions(workflowDefinition, action);

        // Check if any action grants unconditional access (no condition, no step restrictions)
        var hasUnconditionalAccess = allowedActions.Any(a =>
            a.Condition == null &&
            a.Steps.Length == 0 &&
            (a.WorkflowDefinition == null || a.WorkflowDefinition == workflowDefinition));

        if (hasUnconditionalAccess)
            return null;

        var filters = new List<BsonDocument>();
        if (includeDirectRoles)
            filters.AddRange(await BuildInstanceRoleFilters(workflowDefinition, user, action));
        filters.AddRange(await BuildInheritedRoleFilters(workflowDefinition, user, action, ct));

        return filters.Count switch
        {
            0 => new BsonDocument("_id", new BsonDocument("$in", new BsonArray())),
            1 => filters[0],
            _ => new BsonDocument("$or", new BsonArray(filters))
        };
    }

    /// <summary>
    /// Whether the current user can access the workflow overview through global or inherited roles.
    /// Direct instance roles belong in the personal overview and do not qualify here.
    /// </summary>
    public async Task<bool> CanAccessWorkflowOverview(string workflowDefinition, CancellationToken ct)
    {
        var authorizationFilter = await BuildAuthorizationFilter(workflowDefinition, RoleAction.View, ct,
            includeDirectRoles: false);

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

    /// <summary>
    /// Whether the current user can edit at least one instance of the given workflow definition
    /// (either unconditional edit access, or at least one instance they are authorized to edit).
    /// </summary>
    public async Task<bool> HasEditableInstances(string workflowDefinition, CancellationToken ct)
    {
        var authorizationFilter = await BuildAuthorizationFilter(workflowDefinition, RoleAction.Edit, ct);

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
        User user,
        RoleAction action)
    {
        var filters = new List<BsonDocument>();

        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return filters;

        var rolesWithViewAccess = new HashSet<string>();

        var globalRoles = await rightsService.GetGlobalUserRoles();
        foreach (var globalRoleName in globalRoles)
        {
            var role = definition.Roles.GetOrDefault(globalRoleName);
            if (role != null && role.Actions.Any(a =>
                    a.Type == action &&
                    (a.WorkflowDefinition == null || a.WorkflowDefinition == workflowDefinition)))
            {
                rolesWithViewAccess.Add(role.Name);
            }
        }

        rolesWithViewAccess.UnionWith(
            definition.GlobalActions
                .Where(a => a.Type == action)
                .SelectMany(a => a.Roles)
        );

        // Find user type properties that correspond to these roles
        var userProperties = definition.Properties
            .Where(p => p.DataType == DataType.User)
            .Where(p => rolesWithViewAccess.Contains(p.Name))
            .ToList();

        var userId = new ObjectId(user.Id);

        filters.AddRange(
            userProperties.Select(property => new BsonDocument($"Properties.{property.Name}._id", userId))
        );

        return filters;
    }

    private async Task<List<BsonDocument>> BuildInheritedRoleFilters(
        string workflowDefinition,
        User user,
        RoleAction action,
        CancellationToken ct)
    {
        var filters = new List<BsonDocument>();

        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return filters;

        var allowedRoles = definition.Roles
            .Where(r => r.Actions.Any(a =>
                a.Type == action
                && (a.WorkflowDefinition == null
                    || a.WorkflowDefinition == workflowDefinition)))
            .Select(r => r.Name)
            .Distinct()
            .ToArray();

        var referenceProperties = definition.Properties
            .Where(p => p.WorkflowDefinition != null
                        && p.DataType == DataType.Reference)
            .ToArray();

        foreach (var property in referenceProperties)
        {
            if (property.WorkflowDefinition is not { } referencedDefinition)
                continue;

            var matchingIds = new HashSet<string>();

            foreach (var role in allowedRoles)
            {
                matchingIds.UnionWith(await GetInstanceIdsForRole(
                    referencedDefinition,
                    role,
                    user,
                    new HashSet<WorkflowDefinition>(),
                    ct));
            }

            if (matchingIds.Count > 0)
            {
                filters.Add(new BsonDocument(
                    $"Properties.{property.Name}",
                    new BsonDocument("$in", new BsonArray(matchingIds))));
            }
        }

        return filters;
    }

    private async Task<string[]> GetInstanceIdsForRole(
        WorkflowDefinition definition,
        string role,
        User user,
        HashSet<WorkflowDefinition> visited,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!visited.Add(definition))
        {
            throw new InvalidOperationException(
                $"Circular inherited-role references for role '{role}' "
                + $"in workflow definition '{definition.Name}'.");
        }

        var filters = new List<BsonDocument>();

        var roleProperty = definition.Properties.FirstOrDefault(p =>
            p.Name == role
            && p.DataType == DataType.User
            && p.IsInheritableRole == true);

        if (roleProperty != null)
        {
            filters.Add(new BsonDocument(
                $"Properties.{roleProperty.Name}._id",
                new ObjectId(user.Id)));
        }

        var referenceProperties = definition.Properties
            .Where(p => p.WorkflowDefinition != null
                        && p.DataType == DataType.Reference)
            .ToArray();

        foreach (var property in referenceProperties)
        {
            if (property.WorkflowDefinition is not { } referencedDefinition)
                continue;

            var referencedIds = await GetInstanceIdsForRole(
                referencedDefinition,
                role,
                user,
                new HashSet<WorkflowDefinition>(visited),
                ct);

            if (referencedIds.Length > 0)
            {
                filters.Add(new BsonDocument(
                    $"Properties.{property.Name}",
                    new BsonDocument("$in", new BsonArray(referencedIds))));
            }
        }

        if (filters.Count == 0)
            return [];

        var filter = filters.Count == 1
            ? filters[0]
            : new BsonDocument("$or", new BsonArray(filters));

        return await GetMatchingInstanceIds(definition, filter, ct);
    }

    private async Task<string[]> GetMatchingInstanceIds(
        WorkflowDefinition definition,
        BsonDocument filter,
        CancellationToken ct)
    {
        var instances = await workflowInstanceRepository.GetAllByType(
            definition.Name,
            new Dictionary<string, string> { ["_id"] = "$_id" },
            filter,
            ct);

        return instances
            .Select(i => i["_id"].AsObjectId.ToString())
            .ToArray();
    }
}