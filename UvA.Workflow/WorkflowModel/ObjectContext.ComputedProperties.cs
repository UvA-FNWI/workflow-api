using System.Collections;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Persistence;

namespace UvA.Workflow.WorkflowModel;

public partial class ObjectContext
{
    /// <summary>Rebuilds computed values after source data has been loaded or enriched.</summary>
    public void ComputeProperties(WorkflowDefinition definition)
    {
        foreach (var property in definition.ComputedProperties)
            Values.Remove(property.Name);

        foreach (var property in definition.ComputedProperties)
        {
            var expression = property.ComputedExpression!;
            if (expression.Properties.OfType<PropertyLookup>().Any(source => !HasSource(source)))
                continue;

            try
            {
                Values[property.Name] = ConvertComputedValue(expression.Execute(this), property);
            }
            catch (Exception exception)
            {
                throw new InvalidWorkflowStateException(Id ?? "", "InvalidComputedProperty",
                    $"Computed property '{property.Name}' in '{definition.Name}' must return {property.Type}: {exception.Message}");
            }
        }

        bool HasSource(PropertyLookup source)
        {
            if (source.Property == "now") return true;
            if (!Values.ContainsKey(source.Parts[0])) return false;
            return source.Parts.Length == 1 ||
                   definition.Properties.GetOrDefault(source.Parts[0]) is not ReferencePropertyDefinition ||
                   Get(source.Parts[0]) switch
                   {
                       null or Array { Length: 0 } => true,
                       Dictionary<Lookup, object?> reference => reference.ContainsKey(source.Parts[1]),
                       Dictionary<Lookup, object?>?[] references => references.All(reference =>
                           reference == null || reference.ContainsKey(source.Parts[1])),
                       _ => false
                   };
        }
    }

    private static object? ConvertComputedValue(object? value, PropertyDefinition property)
    {
        if (value == null)
            return !property.IsRequired ? null : throw new ArgumentException("A value is required");

        if (!property.IsArray)
            return ConvertComputedScalar(value, property);
        if (value is not IEnumerable values || value is string)
            throw new ArgumentException("Expected a collection");

        var items = values.Cast<object?>().Select(item => item == null
            ? null
            : ConvertComputedScalar(item, property)).ToArray();
        return property.DataType switch
        {
            DataType.String or DataType.Choice => items.Cast<string?>().ToArray(),
            DataType.Reference => items.All(item => item is null or string)
                ? items.Cast<string?>().ToArray()
                : items.Cast<Dictionary<Lookup, object?>?>().ToArray(),
            DataType.User => items.Cast<InstanceUser?>().ToArray(),
            DataType.File => items.Cast<ArtifactInfo?>().ToArray(),
            DataType.Currency => items.Cast<CurrencyAmount?>().ToArray(),
            DataType.Object => items.Cast<Dictionary<string, object?>?>().ToArray(),
            _ => items
        };
    }

    private static object ConvertComputedScalar(object value, PropertyDefinition property)
    {
        return property.DataType switch
        {
            DataType.String or DataType.Reference when value is string => value,
            DataType.String when value is Dictionary<Lookup, object?> reference &&
                                 reference.GetValueOrDefault("Id") is string id => id,
            DataType.Reference when value is Dictionary<Lookup, object?> => value,
            DataType.Choice when value is string name &&
                                 ((ChoicePropertyDefinition)property).Values?.Any(choice => choice.Name == name) == true
                => value,
            DataType.Int when value is int => value,
            DataType.Double when value is int or double => Convert.ToDouble(value),
            DataType.Check when value is bool => value,
            DataType.Date or DataType.DateTime when value is DateTime => value,
            DataType.User when value is InstanceUser => value,
            DataType.Currency when value is CurrencyAmount => value,
            DataType.File when value is ArtifactInfo => value,
            DataType.Object when value is Dictionary<string, object?> => value,
            _ => throw new ArgumentException($"Unexpected value of type {value.GetType().Name}")
        };
    }
}