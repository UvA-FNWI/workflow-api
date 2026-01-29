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

    private async Task<List<BsonDocument>> BuildInstanceRoleFilters(
        string workflowDefinition,
        User user)
    {
        var filters = new List<BsonDocument>();

        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return filters;

        var rolesWithViewAccess = new HashSet<string>();

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
        CancellationToken ct)
    {
        var filters = new List<BsonDocument>();

        if (!modelService.WorkflowDefinitions.TryGetValue(workflowDefinition, out var definition))
            return filters;

        var propertiesWithInheritedRoles = definition.Properties
            .Where(p => p.InheritedRoles.Any())
            .ToList();

        if (propertiesWithInheritedRoles.Count == 0)
            return filters;

        foreach (var property in propertiesWithInheritedRoles)
        {
            foreach (var inheritedRole in property.InheritedRoles)
            {
                var referencedWorkflowDef = property.WorkflowDefinition?.Name;
                if (referencedWorkflowDef == null)
                    continue;

                var roleProperty = property.WorkflowDefinition?.Properties
                    .FirstOrDefault(p => p.Name == inheritedRole && p.DataType == DataType.User);

                if (roleProperty == null)
                    continue;

                var userId = new ObjectId(user.Id);
                var rolePropertyPath = $"Properties.{roleProperty.Name}._id";

                // Create filter for referenced instances
                var referencedFilter = new BsonDocument(rolePropertyPath, userId);

                // Query to get IDs of referenced instances where user has the role
                var referencedInstanceIds = await workflowInstanceRepository.GetAllByType(
                    referencedWorkflowDef,
                    new Dictionary<string, string> { ["_id"] = "$_id" },
                    referencedFilter,
                    ct);

                var matchingIds = referencedInstanceIds
                    .Select(r => r["_id"].AsObjectId)
                    .ToList();

                if (matchingIds.Any())
                {
                    var propertyPath = $"Properties.{property.Name}";
                    filters.Add(new BsonDocument(propertyPath,
                        new BsonDocument("$in", new BsonArray(matchingIds.Select(id => id.ToString())))));
                }
            }
        }

        return filters;
    }
}