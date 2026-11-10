using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Core;

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
    /// Reads the templates from the common templates and the definition folder, returning a dictionary keyed by template name.
    /// </summary>
    /// <returns></returns>
    private Dictionary<string, TTemplate> ReadTemplates<TTemplate>(
        IEnumerable<TTemplate> commonTemplates, string definitionFolder)
        where TTemplate : ITemplate
    {
        var templatesByName = commonTemplates.ToDictionary(template => template.Name);
        foreach (var template in Read<TTemplate>(definitionFolder))
            templatesByName[template.Name] = template;

        return templatesByName;
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

        var template = GetTemplate(step, stepTemplatesByName, definitionName);
        var resolved = Instantiate(template, step, parentValues, out var substituted);

        ApplyInheritance(step, resolved);
        step.DeclaredKeys.UnionWith(ReadTopLevelKeys(substituted));

        // Consumer properties replace template properties with the same name.
        var consumerPropertyNames = step.Properties.Select(p => p.Name).ToHashSet();
        step.Properties =
        [
            .. resolved.Properties.Where(p => !consumerPropertyNames.Contains(p.Name)),
            .. step.Properties
        ];

        return step;
    }

    /// <summary>
    /// Resolves the form template for every form in a definition that declares one. Unlike steps, forms have
    /// no parent/child hierarchy, so each form is resolved independently and inherits no values.
    /// </summary>
    private List<Form> ResolveFormTemplates(List<Form> forms,
        Dictionary<string, FormTemplate> formTemplatesByName, string definitionName)
    {
        var duplicateNames = forms.GroupBy(f => f.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateNames.Count > 0)
            throw new Exception(
                $"Definition '{definitionName}' declares duplicate form(s): {string.Join(", ", duplicateNames)}");

        foreach (var form in forms.Where(f => !string.IsNullOrWhiteSpace(f.TemplateKey)))
        {
            var template = GetTemplate(form, formTemplatesByName, definitionName);
            var resolved = Instantiate(template, form, null, out var substituted);

            ApplyInheritance(form, resolved);
            form.DeclaredKeys.UnionWith(ReadTopLevelKeys(substituted));
        }

        return forms;
    }

    /// <summary>
    /// Instantiates a template for a consuming step or form: resolves the parameter values, substitutes them
    /// into the template's source YAML and deserializes the result. The caller is responsible for merging
    /// the returned instance into its consumer, since that differs per type.
    /// </summary>
    private TTemplate Instantiate<TTemplate>(TTemplate template, ITemplateConsumer consumer,
        IReadOnlyDictionary<string, object>? parentValues, out string substituted)
        where TTemplate : ITemplate
    {
        var values = ResolveTemplateValues(template.Parameters, consumer, parentValues);
        foreach (var parameter in template.Parameters.Where(p => p.UnderlyingType == "LocalString"))
            if (values.TryGetValue(parameter.Name, out var value) && value is not BilingualString)
                values[parameter.Name] = value is string text
                    ? (BilingualString)text
                    : _deserializer.Deserialize<BilingualString>(new SerializerBuilder().Build().Serialize(value));

        substituted = SubstituteTemplateValues(template.RootFile, template.Name, values);
        consumer.ResolvedTemplateValues = values;

        try
        {
            return _deserializer.Deserialize<TTemplate>(substituted);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"Failed to resolve template '{template.Name}' for '{consumer.Name}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Looks up the template a step or form refers to with <c>template:</c>.
    /// </summary>
    private static TTemplate GetTemplate<TTemplate>(ITemplateConsumer consumer,
        Dictionary<string, TTemplate> templatesByName, string definitionName) where TTemplate : ITemplate
        => templatesByName.TryGetValue(consumer.TemplateKey!, out var template)
            ? template
            : throw new Exception(
                $"'{consumer.Name}' in definition '{definitionName}' references unknown template " +
                $"'{consumer.TemplateKey}'");

    /// <summary>
    /// Resolves the values for a template from the values the consuming step or form provides. A missing
    /// value falls back to a value inherited from the parent step (steps only), then to the parameter's
    /// default. The implicit <c>prefix</c> value defaults to everything before the first underscore in the
    /// consumer's name.
    /// </summary>
    private static Dictionary<string, object> ResolveTemplateValues(List<TemplateParameter> parameters,
        ITemplateConsumer consumer, IReadOnlyDictionary<string, object>? parentValues)
    {
        var providedValues =
            new Dictionary<string, object>(consumer.TemplateValues ?? [], StringComparer.OrdinalIgnoreCase);
        providedValues.TryAdd("prefix", GetPrefix(consumer.Name));

        var resolvedValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["prefix"] = providedValues["prefix"]
        };

        foreach (var param in parameters)
        {
            if (providedValues.TryGetValue(param.Name, out var value))
                resolvedValues[param.Name] = value;
            else if (parentValues != null && parentValues.TryGetValue(param.Name, out var inherited))
                resolvedValues[param.Name] = inherited;
            else if (param.Default is not null)
                resolvedValues[param.Name] = param.Default;
            else if (param.IsRequired)
                throw new ArgumentException(
                    $"Missing required template parameter '{param.Name}' for '{consumer.Name}'");
        }

        var unknownValues = providedValues.Keys.Except(resolvedValues.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        if (unknownValues.Count > 0)
            throw new ArgumentException(
                $"Unknown template parameters '{string.Join(", ", unknownValues)}' for '{consumer.Name}'");

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

        var replacements = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values)
        {
            if (value is BilingualString localized)
            {
                replacements[$"{name}.en"] = localized.En;
                replacements[$"{name}.nl"] = localized.ForLanguage("nl");
            }
            else
                replacements[name] = value;
        }

        var yaml = new YamlStream();
        yaml.Load(new StringReader(rawYaml));
        foreach (var scalar in yaml.Documents[0].AllNodes.OfType<YamlScalarNode>())
        {
            if (scalar.Value is null || !TokenPattern().IsMatch(scalar.Value))
                continue;

            scalar.Value = TokenPattern().Replace(scalar.Value, match =>
            {
                var name = match.Groups[1].Value;
                if (!replacements.TryGetValue(name, out var value))
                    throw new ArgumentException($"Missing template value '{name}' for template '{templateName}'");
                return value?.ToString() ?? "";
            });
            scalar.Style = ScalarStyle.DoubleQuoted;
        }

        using var writer = new StringWriter();
        yaml.Save(writer, false);
        return writer.ToString();
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
    /// step or an explicit <c>children:</c> override, the list is known directly. Otherwise, the child names
    /// come from the template's <c>children:</c> list, which by convention only references
    /// <c>${prefix}</c> — never any other template parameter — since a step's own parent must not need to be
    /// resolved first just to find out which children it has.
    /// </summary>
    private static string[] GetChildStepNames(Step step, Dictionary<string, StepTemplate> stepTemplatesByName,
        string definitionName)
    {
        if (string.IsNullOrWhiteSpace(step.TemplateKey) || step.Declared(s => s.ChildNames))
            return step.ChildNames;

        if (!stepTemplatesByName.TryGetValue(step.TemplateKey, out var template))
            throw new Exception(
                $"Step '{step.Name}' in definition '{definitionName}' references unknown template '{step.TemplateKey}'");

        var providedValues =
            new Dictionary<string, object>(step.TemplateValues ?? [], StringComparer.OrdinalIgnoreCase);
        var prefix = providedValues.TryGetValue("prefix", out var value)
            ? value.ToString() ?? ""
            : GetPrefix(step.Name);

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