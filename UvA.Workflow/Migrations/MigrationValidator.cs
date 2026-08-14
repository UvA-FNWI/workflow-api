using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Migrations;

public static class MigrationValidator
{
    public static void ValidatePropertyRename(
        ModelParser active,
        ModelParser target,
        string workflowDefinition,
        string oldPath,
        string newPath)
    {
        ValidatePath(oldPath, nameof(oldPath));
        ValidatePath(newPath, nameof(newPath));
        if (oldPath == newPath)
            throw new InvalidOperationException("The old and new property paths must be different");

        if (!active.WorkflowDefinitions.TryGetValue(workflowDefinition, out var activeDefinition))
            throw new InvalidOperationException(
                $"Workflow '{workflowDefinition}' does not exist in the active configuration");
        if (!target.WorkflowDefinitions.TryGetValue(workflowDefinition, out var targetDefinition))
            throw new InvalidOperationException(
                $"Workflow '{workflowDefinition}' does not exist in the target configuration");

        var oldProperty = ResolveProperty(activeDefinition, oldPath)
                          ?? throw new InvalidOperationException(
                              $"Property '{oldPath}' does not exist in the active workflow '{workflowDefinition}'");
        var newProperty = ResolveProperty(targetDefinition, newPath)
                          ?? throw new InvalidOperationException(
                              $"Property '{newPath}' does not exist in the target workflow '{workflowDefinition}'");
        if (ResolveProperty(targetDefinition, oldPath) != null)
            throw new InvalidOperationException(
                $"Property '{oldPath}' must be removed from the target workflow '{workflowDefinition}'");
        if (oldProperty.UnderlyingType != newProperty.UnderlyingType || oldProperty.IsArray != newProperty.IsArray)
            throw new InvalidOperationException(
                $"The rename changes the stored type from '{oldProperty.Type}' to '{newProperty.Type}'");
    }

    private static void ValidatePath(string path, string field)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('.') || path.EndsWith('.') ||
            path.Split('.').Any(part => string.IsNullOrWhiteSpace(part) || part.StartsWith('$')))
            throw new InvalidOperationException($"Invalid {field} '{path}'");
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