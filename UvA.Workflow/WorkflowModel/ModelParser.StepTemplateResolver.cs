using System.Text.RegularExpressions;

namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    [GeneratedRegex(@"\$\{\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\}")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// The default "prefix" value for a step: everything before the first underscore in its name, or the
    /// full name if it has no underscore.
    /// </summary>
    private static string GetPrefix(string stepName)
    {
        var underscoreIndex = stepName.IndexOf('_');
        return underscoreIndex < 0 ? stepName : stepName[..underscoreIndex];
    }

    /// <summary>
    /// Resolves the step templates for all of a definition's steps. Steps are resolved parent-first (based on
    /// which step's template declares them as a child — see <see cref="GetChildStepNames"/>), so that a
    /// parent's resolved template values are available to pass down to its children as inheritable fallbacks
    /// (see <see cref="Step.ResolvedTemplateValues"/> and <see cref="ResolveTemplateValues"/>).
    /// </summary>
    private List<Step> ResolveStepTemplates(List<Step> steps, Dictionary<string, StepTemplate> stepTemplatesByName,
        string definitionName)
    {
        var stepsByName = GetStepsByName(steps, definitionName);
        var parentNameByChildName = GetParentNameByChildName(steps, stepTemplatesByName, definitionName);
        var resolvedStepsByName = new Dictionary<string, Step>();

        foreach (var stepName in stepsByName.Keys)
            ResolveStep(stepName, stepsByName, parentNameByChildName, stepTemplatesByName, resolvedStepsByName,
                new HashSet<string>(), definitionName);

        // Preserve the original step order.
        return steps.Select(s => resolvedStepsByName[s.Name]).ToList();
    }

    /// <summary>
    /// Resolves a single step (and, recursively, its parent first if it has one) and records the result in
    /// <paramref name="resolvedStepsByName"/>. <paramref name="stepsBeingResolved"/> tracks the steps
    /// currently on the call stack, so a circular parent/child relationship is detected instead of causing a
    /// stack overflow.
    /// </summary>
    private Step ResolveStep(string stepName, Dictionary<string, Step> stepsByName,
        Dictionary<string, string> parentNameByChildName, Dictionary<string, StepTemplate> stepTemplatesByName,
        Dictionary<string, Step> resolvedStepsByName, HashSet<string> stepsBeingResolved, string definitionName)
    {
        if (resolvedStepsByName.TryGetValue(stepName, out var alreadyResolved))
            return alreadyResolved;

        if (!stepsBeingResolved.Add(stepName))
            throw new Exception(
                $"Cyclic parent/child relationship detected involving step '{stepName}' in definition '{definitionName}'");

        var step = stepsByName[stepName];

        // Resolve the parent first (if any), so we can offer its resolved values to this step as inheritable fallbacks.
        Dictionary<string, object>? parentValues = null;
        if (parentNameByChildName.TryGetValue(stepName, out var parentName))
        {
            var resolvedParent = ResolveStep(parentName, stepsByName, parentNameByChildName, stepTemplatesByName,
                resolvedStepsByName, stepsBeingResolved, definitionName);
            parentValues = resolvedParent.ResolvedTemplateValues;
        }

        var resolvedStep = ResolveStepTemplate(step, stepTemplatesByName, parentValues, definitionName);

        resolvedStepsByName[stepName] = resolvedStep;
        stepsBeingResolved.Remove(stepName);
        return resolvedStep;
    }

    /// <summary>
    /// Resolves a single step's template if it has one. Steps without a <c>template:</c> are returned as-is.
    /// </summary>
    private Step ResolveStepTemplate(Step step, Dictionary<string, StepTemplate> stepTemplatesByName,
        Dictionary<string, object>? parentValues, string definitionName)
    {
        if (string.IsNullOrWhiteSpace(step.TemplateKey))
            return step;

        if (!stepTemplatesByName.TryGetValue(step.TemplateKey, out var template))
            throw new Exception(
                $"Step '{step.Name}' in definition '{definitionName}' references unknown template '{step.TemplateKey}'");

        return ResolveTemplateToStep(step, template, _deserializer, parentValues);
    }

    /// <summary>
    /// Resolves a step template into a step.
    /// </summary>
    private static Step ResolveTemplateToStep(Step consumer, StepTemplate template, IDeserializer deserializer,
        IReadOnlyDictionary<string, object>? parentValues = null)
    {
        var values = ResolveTemplateValues(template, consumer, parentValues);
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
        resolved.Title = consumer.Title;
        resolved.TemplateKey = consumer.TemplateKey;
        resolved.TemplateValues = consumer.TemplateValues;
        resolved.ResolvedTemplateValues = values;
        resolved.DeclaredKeys = consumer.DeclaredKeys;
        resolved.Properties = [..resolved.Properties, ..consumer.Properties];
        resolved.Events = [..resolved.Events, ..consumer.Events];

        return resolved;
    }

    /// <summary>
    /// Resolves the values for a step template from the provided values.
    /// </summary>
    private static Dictionary<string, object> ResolveTemplateValues(StepTemplate template, Step step,
        IReadOnlyDictionary<string, object>? parentValues)
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
        providedValues.TryAdd("prefix", GetPrefix(step.Name));

        var resolvedValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["prefix"] = providedValues["prefix"]
        };

        foreach (var param in template.Parameters)
        {
            if (providedValues.TryGetValue(param.Name, out var value))
                resolvedValues[param.Name] = value;
            else if (parentValues != null && parentValues.TryGetValue(param.Name, out var inherited))
                resolvedValues[param.Name] = inherited;
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


    private static Dictionary<string, Step> GetStepsByName(List<Step> steps, string definitionName)
    {
        var duplicateNames = steps
            .GroupBy(s => s.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateNames.Count > 0)
            throw new Exception(
                $"Definition '{definitionName}' declares duplicate step(s): {string.Join(", ", duplicateNames)}");

        return steps.ToDictionary(s => s.Name);
    }

    /// <summary>
    /// Inverts every step's <c>children:</c> list into a child-name -> parent-name lookup.
    /// </summary>
    private static Dictionary<string, string> GetParentNameByChildName(List<Step> steps,
        Dictionary<string, StepTemplate> stepTemplatesByName, string definitionName)
    {
        var parentNameByChildName = new Dictionary<string, string>();

        foreach (var step in steps)
        foreach (var childName in GetChildStepNames(step, stepTemplatesByName, definitionName))
        {
            if (parentNameByChildName.TryGetValue(childName, out var existingParentName) &&
                existingParentName != step.Name)
                throw new Exception(
                    $"Step '{childName}' in definition '{definitionName}' is declared as a child of both " +
                    $"'{existingParentName}' and '{step.Name}'");

            parentNameByChildName[childName] = step.Name;
        }

        return parentNameByChildName;
    }

    /// <summary>
    /// Determines a step's child step names before the step is fully resolved. For a plain (non-templated)
    /// step, its own <c>children:</c> list is already known directly. For a templated step, the child names
    /// come from the template's <c>children:</c> list, which by convention only references
    /// <c>${prefix}</c> — never any other template parameter — since a step's own parent must not need to be
    /// resolved first just to find out which children it has.
    /// </summary>
    private static string[] GetChildStepNames(Step step, Dictionary<string, StepTemplate> stepTemplatesByName,
        string definitionName)
    {
        if (string.IsNullOrWhiteSpace(step.TemplateKey))
            return step.ChildNames;

        if (!stepTemplatesByName.TryGetValue(step.TemplateKey, out var template))
            throw new Exception(
                $"Step '{step.Name}' in definition '{definitionName}' references unknown template '{step.TemplateKey}'");

        var prefix = GetPrefix(step.Name);

        return template.ChildNames
            .Select(childName => TokenPattern().Replace(childName, match =>
            {
                var tokenName = match.Groups[1].Value;
                if (tokenName != "prefix")
                    throw new Exception(
                        $"Template '{template.Name}' declares a child name using '${{{tokenName}}}', but child " +
                        $"names may only use '${{prefix}}' (needed to determine parent/child order for step " +
                        $"'{step.Name}' before the step is fully resolved)");
                return prefix;
            }))
            .ToArray();
    }
}