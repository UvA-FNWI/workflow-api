using System.Text.RegularExpressions;

namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    [GeneratedRegex(@"\$\{\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\}")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// Resolves the values for a step template from the provided values.
    /// </summary>
    private static Dictionary<string, object> ResolveTemplateValues(StepTemplate template, Step step)
    {
        var values = step.TemplateValues ?? [];

        var duplicates = values
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
            throw new ArgumentException(
                $"Duplicate template parameters '{string.Join(", ", duplicates)}' for step '{step.Name}'");

        var providedValues = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);
        var resolvedValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var param in template.Parameters)
        {
            if (providedValues.TryGetValue(param.Name, out var value))
                resolvedValues[param.Name] = value;
            else if (param.Default is not null)
                resolvedValues[param.Name] = param.Default;
            else if (param.IsRequired)
                throw new ArgumentException(
                    $"Missing required template parameter '{param.Name}' for step '{step.Name}'");
        }

        var unknownValues = providedValues.Keys.Except(resolvedValues.Keys).ToList();
        if (unknownValues.Count > 0)
            throw new ArgumentException(
                $"Unknown template parameters '{string.Join(", ", unknownValues)}' for step '{step.Name}'");

        return resolvedValues;
    }

    /// <summary>
    /// Substitutes the values for a template into a YAML string.
    /// </summary>
    /// <param name="rawYaml"></param>
    /// <param name="templateName"></param>
    /// <param name="values"></param>
    private static string SubstituteTemplateValues(string rawYaml, string templateName,
        IReadOnlyDictionary<string, object> values)
    {
        if (string.IsNullOrWhiteSpace(rawYaml))
            throw new ArgumentException(
                $"Template '{templateName}' has no source YAML content. Ensure StepTemplate.RootFile is populated.");

        return TokenPattern().Replace(rawYaml, match =>
        {
            var name = match.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
                throw new ArgumentException($"Missing template value '{name}' for template '{templateName}'");
            return value.ToString() ?? "";
        });
    }

    /// <summary>
    /// Resolves a step template into a step.
    /// </summary>
    private static Step ResolveTemplate(Step consumer, StepTemplate template, IDeserializer deserializer)
    {
        var values = ResolveTemplateValues(template, consumer);
        var substituted = SubstituteTemplateValues(template.RootFile, template.Name, values);

        Step resolved;
        try
        {
            resolved = deserializer.Deserialize<StepTemplate>(substituted);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"Failed to resolve template '{template.Name}' for step '{consumer.Name}': {ex.Message}", ex);
        }

        resolved.Name = consumer.Name;
        resolved.TemplateKey = consumer.TemplateKey;
        resolved.TemplateValues = consumer.TemplateValues;
        resolved.DeclaredKeys = consumer.DeclaredKeys;
        resolved.Properties = [..resolved.Properties, ..consumer.Properties];
        resolved.Events = [..resolved.Events, ..consumer.Events];

        return resolved;
    }
}