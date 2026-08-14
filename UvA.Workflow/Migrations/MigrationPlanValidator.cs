using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Migrations;

/// <summary>Validates migration plans within one model and across an active/pending model pair.</summary>
public static class MigrationPlanValidator
{
    public static void Validate(ModelParser parser)
    {
        var plans = parser.Migrations.ToArray();
        var duplicate = plans.GroupBy(plan => plan.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new Exception($"Property migration '{duplicate.Key}' is defined more than once");

        foreach (var plan in plans)
        {
            if (plan.Kind != MigrationDefinition.RenamePropertyKind)
                throw new Exception(
                    $"Property migration '{plan.Id}' has unsupported kind '{plan.Kind}'. " +
                    $"Expected '{MigrationDefinition.RenamePropertyKind}'.");
            ValidatePath(plan, plan.OldPath, "oldPath");
            ValidatePath(plan, plan.NewPath, "newPath");
            if (plan.OldPath == plan.NewPath)
                throw new Exception($"Property migration '{plan.Id}' must use different oldPath and newPath values");

            var definition = parser.WorkflowDefinitions[plan.WorkflowDefinition];
            if (ResolveProperty(definition, plan.NewPath) == null)
                throw new Exception(
                    $"Property migration '{plan.Id}' references unknown newPath '{plan.NewPath}' " +
                    $"in workflow '{plan.WorkflowDefinition}'");
        }

        foreach (var workflowPlans in plans.GroupBy(plan => plan.WorkflowDefinition))
        {
            var duplicateOldPath = workflowPlans.GroupBy(plan => plan.OldPath)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateOldPath != null)
                throw new Exception(
                    $"Workflow '{workflowPlans.Key}' renames '{duplicateOldPath.Key}' more than once");

            var duplicateNewPath = workflowPlans.GroupBy(plan => plan.NewPath)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateNewPath != null)
                throw new Exception(
                    $"Workflow '{workflowPlans.Key}' has multiple migrations targeting '{duplicateNewPath.Key}'");

            var targets = workflowPlans.ToDictionary(plan => plan.OldPath, plan => plan.NewPath,
                StringComparer.Ordinal);
            foreach (var origin in targets.Keys)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var current = origin;
                while (targets.TryGetValue(current, out var next))
                {
                    if (!seen.Add(current))
                        throw new Exception(
                            $"Workflow '{workflowPlans.Key}' contains a property migration cycle at '{current}'");
                    current = next;
                }
            }
        }
    }

    /// <summary>
    /// Validates plans introduced by a pending configuration against the currently active model and
    /// returns the plans that require administrator approval.
    /// </summary>
    public static IReadOnlyList<MigrationDefinition> Compare(
        ModelParser active,
        ModelParser pending)
    {
        var activePlans = active.Migrations.ToDictionary(plan => plan.Id);
        var pendingPlans = pending.Migrations.ToDictionary(plan => plan.Id);

        foreach (var activePlan in activePlans.Values)
        {
            if (!pendingPlans.TryGetValue(activePlan.Id, out var retained))
                throw new Exception(
                    $"Activated property migration '{activePlan.Id}' cannot be removed from configuration");
            if (retained.Checksum != activePlan.Checksum)
                throw new Exception(
                    $"Activated property migration '{activePlan.Id}' cannot be edited; add a new migration instead");
        }

        var introduced = pendingPlans.Values.Where(plan => !activePlans.ContainsKey(plan.Id)).ToArray();
        foreach (var plan in introduced)
        {
            if (!active.WorkflowDefinitions.TryGetValue(plan.WorkflowDefinition, out var activeDefinition))
                throw new Exception(
                    $"Property migration '{plan.Id}' references workflow '{plan.WorkflowDefinition}', " +
                    "which does not exist in the active configuration");

            var oldProperty = ResolveProperty(activeDefinition, plan.OldPath)
                              ?? throw new Exception(
                                  $"Property migration '{plan.Id}' references unknown active oldPath " +
                                  $"'{plan.OldPath}'");
            var pendingDefinition = pending.WorkflowDefinitions[plan.WorkflowDefinition];
            var newProperty = ResolveProperty(pendingDefinition, plan.NewPath)!;

            if (ResolveProperty(pendingDefinition, plan.OldPath) != null)
                throw new Exception(
                    $"Property migration '{plan.Id}' leaves oldPath '{plan.OldPath}' in the pending " +
                    "configuration; remove it when introducing the renamed property");

            if (oldProperty.UnderlyingType != newProperty.UnderlyingType ||
                oldProperty.IsArray != newProperty.IsArray)
                throw new Exception(
                    $"Property migration '{plan.Id}' changes the stored type from '{oldProperty.Type}' " +
                    $"to '{newProperty.Type}'. A rename migration may only change requiredness.");
        }

        return introduced;
    }

    private static void ValidatePath(MigrationDefinition plan, string? path, string field)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('.') || path.EndsWith('.') ||
            path.Split('.').Any(part => string.IsNullOrWhiteSpace(part) || part.StartsWith('$')))
            throw new Exception($"Property migration '{plan.Id}' has invalid {field} '{path}'");
    }

    private static PropertyDefinition? ResolveProperty(WorkflowDefinition definition, string path)
    {
        var type = definition;
        PropertyDefinition? property = null;
        var parts = path.Split('.');
        for (var index = 0; index < parts.Length; index++)
        {
            property = type.Properties.GetOrDefault(parts[index]);
            if (property == null)
                return null;
            if (index < parts.Length - 1)
            {
                if (property.WorkflowDefinition == null)
                    return null;
                type = property.WorkflowDefinition;
            }
        }

        return property;
    }
}