using UvA.Workflow.Expressions;

namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    private static void PreparePropertyDependencies(WorkflowDefinition definition)
    {
        var knownSources = definition.Properties.Select(property => property.Name)
            .Concat(["now", "Id", "CurrentStep", "CreateDate", "LastEvent", "Assessment"])
            .Concat(definition.Events.SelectMany(ev => new[] { ev.Name + "Event", ev.Name + "EventActive" }))
            .ToHashSet();
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();
        foreach (var property in definition.Properties.Where(property => property.IsComputed))
            Visit(property);

        foreach (var property in definition.Properties)
        foreach (var source in definition.GetSourceLookups(property.Conditions.SelectMany(c => c.Properties))
                     .OfType<PropertyLookup>().SelectMany(lookup => lookup.Parts).Distinct())
        {
            var dependents = definition.Properties.GetOrDefault(source)?.DependentQuestions;
            if (dependents != null && !dependents.Contains(property)) dependents.Add(property);
        }

        void Visit(PropertyDefinition property)
        {
            if (visited.Contains(property.Name)) return;
            if (!visiting.Add(property.Name))
                throw new Exception($"Computed property dependency cycle in '{definition.Name}' at '{property.Name}'");
            try
            {
                if (string.IsNullOrWhiteSpace(property.Expression))
                    throw new Exception("Expression is empty");
                property.ComputedExpression!.Validate();
                if (property is ReferencePropertyDefinition &&
                    (property.ComputedExpression is not Identifier(var name) ||
                     definition.Properties.GetOrDefault(name) is not ReferencePropertyDefinition referenceSource ||
                     referenceSource.UnderlyingType != property.UnderlyingType ||
                     referenceSource.IsArray != property.IsArray))
                    throw new Exception(
                        "Computed references must directly alias a reference property of the same type");
                foreach (var source in property.ComputedExpression.Properties.OfType<PropertyLookup>())
                {
                    var root = source.Parts[0];
                    if (!knownSources.Contains(root))
                        throw new Exception($"Unknown source property '{root}'");
                }
            }
            catch (Exception exception)
            {
                throw new Exception($"Invalid expression for computed property '{property.Name}': {exception.Message}",
                    exception);
            }

            foreach (var source in property.ComputedExpression!.Properties.OfType<PropertyLookup>())
                if (definition.Properties.GetOrDefault(source.Parts[0]) is { IsComputed: true } dependency)
                    Visit(dependency);
            visiting.Remove(property.Name);
            visited.Add(property.Name);
            definition.ComputedProperties.Add(property);
        }
    }

    private static void ValidateComputedReads(WorkflowDefinition definition)
    {
        foreach (var property in definition.ComputedProperties)
        {
            try
            {
                definition.ValidateReadLookups(property.ComputedExpression!.Properties);
            }
            catch (Exception exception)
            {
                throw new Exception(
                    $"Invalid expression for computed property '{property.Name}' in '{definition.Name}': {exception.Message}",
                    exception);
            }
        }

        var permissionLookups = definition.PermissionAndStepLookups.ToArray();
        try
        {
            definition.ValidateReadLookups(permissionLookups, allowAssessment: false);
        }
        catch (Exception exception)
        {
            throw new Exception($"Invalid permission rule in '{definition.Name}': {exception.Message}", exception);
        }
    }

    private void EnsureStoredProperty(WorkflowDefinition definition, string path, string usage)
    {
        var current = definition;
        foreach (var name in path.Split('.'))
        {
            var property = current.Properties.GetOrDefault(name);
            if (property?.IsComputed == true)
                throw new Exception($"{usage} cannot write computed property '{path}' in '{definition.Name}'");
            if (property is not WorkflowPropertyDefinition workflow) break;
            var next = workflow.WorkflowDefinition ?? WorkflowDefinitions.GetValueOrDefault(workflow.UnderlyingType);
            if (next == null) break;
            current = next;
        }
    }

    private void ValidateComputedWrites(WorkflowDefinition definition)
    {
        foreach (var form in definition.Forms.Where(form => form.PropertyName != null))
            EnsureStoredProperty(definition, form.PropertyName!, $"Form '{form.Name}'");
        foreach (var screen in definition.Screens)
        {
            foreach (var property in screen.BulkEdit?.EditableProperties ?? [])
                EnsureStoredProperty(definition, property, $"Bulk edit on '{screen.Name}'");
            if (screen.BulkEdit?.Identifier != null)
                EnsureStoredProperty(definition, screen.BulkEdit.Identifier.Property,
                    $"Bulk edit identifier on '{screen.Name}'");
        }

        foreach (var user in definition.EditableRelatedUsers)
            EnsureStoredProperty(definition, user.Property, "Editable related user");

        var effects = definition.Roles.SelectMany(role => role.Actions).Concat(definition.AllActions)
            .SelectMany(action => action.OnAction)
            .Concat(definition.Forms.SelectMany(form => form.OnSave.Concat(form.OnSubmit)))
            .Concat(definition.Properties.SelectMany(property => property.OnSave));
        foreach (var effect in effects)
        {
            if (effect.SetProperty != null)
                EnsureStoredProperty(definition, effect.SetProperty.Property, "setProperty");
            if (effect.CreateExternalUserAccount != null)
                EnsureStoredProperty(definition, effect.CreateExternalUserAccount.Role, "createExternalUserAccount");
        }
    }
}