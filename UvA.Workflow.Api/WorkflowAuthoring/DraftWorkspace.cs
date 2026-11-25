using System.Text.RegularExpressions;
using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.WorkflowModel.Conditions;
using YamlDotNet.RepresentationModel;
using WorkflowAction = UvA.Workflow.WorkflowModel.Action;

namespace UvA.Workflow.Api.WorkflowAuthoring;

public sealed record WorkflowDraftWorkspace(
    WorkflowDraft Draft,
    DraftFormReference[] Forms,
    Dictionary<string, string> Files,
    string[] EditablePaths,
    DraftStepReference[] Steps);

public sealed record DraftFormChange(string Path, FormDto? Before, FormDto? After);

public sealed record DraftStepReference(
    string Name,
    BilingualString Title,
    DraftStepReference[] Children,
    DraftStepAction[] Actions,
    DraftCompletionRule? Completion);

public sealed record DraftStepAction(
    RoleAction Type,
    BilingualString? Label,
    BilingualString[] Roles,
    DraftActionForm[] Forms,
    bool AllForms,
    bool Conditional);

public sealed record DraftActionForm(string Name, BilingualString Title);

public sealed record DraftCompletionRule(
    string Kind,
    bool Negated,
    BilingualString? Label,
    DraftCompletionRule[] Children,
    string? Operator = null,
    string? Operand = null);

public sealed record DraftFormReference(
    string Path,
    string Name,
    BilingualString Title,
    FormLayout Layout,
    DraftFormStepReference[] Steps);

public sealed record DraftFormStepReference(string Name, BilingualString[] ActionLabels);

internal sealed partial class DraftWorkspace
{
    private static readonly JsonSerializerOptions PreviewJson = new()
    {
        TypeInfoResolver = new QuestionJsonTypeInfoResolver()
    };

    private static readonly string[] EditablePageFields = ["name", "title", "elements"];
    private static readonly string[] EditableFormFields = ["pages", "title", "layout"];
    private static readonly string[] FormLayouts = Enum.GetNames<FormLayout>();
    private static readonly string[] EditableCalloutFields = ["variant", "title", "text"];
    private static readonly string[] CalloutVariants = Enum.GetNames<CalloutVariant>();

    private static readonly string[] EditablePropertyFields =
    [
        "name", "type", "text", "description", "control", "allowsExternalUsers", "maximumSize", "allowedTypes", "values"
    ];

    private readonly WorkflowAuthoringProject _project;
    private readonly WorkflowDefinition _definition;
    private readonly Dictionary<string, Form> _forms;
    private readonly string[] _definitionFolders;
    private readonly bool _shared;

    public DraftWorkspace(Dictionary<string, string> files, WorkflowAuthoringProject project)
    {
        Files = files;
        _project = project;
        Parser = new ModelParser(new DictionaryProvider(files));
        var definitions = Parser.WorkflowDefinitions;
        _definition = definitions.Values.Single(definition => Folder(definition) == project.Folder);
        _shared = definitions.Values.Any(definition => definition.InheritsFrom == _definition.Name);
        var folders = new List<string>();
        for (var owner = _definition;
             owner is not null;
             owner = owner.InheritsFrom is { } parent ? definitions[parent] : null)
            folders.Add(Folder(owner));
        _definitionFolders = folders.ToArray();

        _forms = files.Keys.Where(path => IsDirectYaml(path, project.Folder + "/Forms")).Order(StringComparer.Ordinal)
            .ToDictionary(path => path, path =>
            {
                var name = DraftYaml.Name(DraftYaml.ParseRoot(files[path])) ?? Path.GetFileNameWithoutExtension(path);
                return _definition.Forms.Single(form => form.Name == name);
            }, StringComparer.Ordinal);
    }

    public Dictionary<string, string> Files { get; }
    public ModelParser Parser { get; }
    public WorkflowDefinition Definition => _definition;

    public bool HasEditableForms => !_shared && _forms.Count > 0;

    public WorkflowDraftWorkspace View(WorkflowDraft draft, bool canEdit)
    {
        var forms = _forms.Select(entry => new DraftFormReference(entry.Key, entry.Value.Name,
            entry.Value.DisplayName, entry.Value.Layout, FormSteps(entry.Value).ToArray())).ToArray();
        var files = canEdit && HasEditableForms
            ? Files.Where(file => _definitionFolders.Any(folder =>
                    IsFormFile(file.Key, folder) || IsPropertyFile(file.Key, folder) ||
                    IsDirectYaml(file.Key, folder + "/ValueSets")) || IsDirectYaml(file.Key, "Common/ValueSets"))
                .ToDictionary(file => file.Key, file => file.Value, StringComparer.Ordinal)
            : new Dictionary<string, string>();
        return new WorkflowDraftWorkspace(draft, forms, files, files.Keys.Where(IsEditablePath).ToArray(),
            _definition.Steps.Select(StepReference).ToArray());
    }

    private DraftStepReference StepReference(Step step) =>
        new(step.Name, step.DisplayTitle, step.Children.Select(StepReference).ToArray(),
            StepActions(step).Select(StepAction).ToArray(), StepCompletion(step));

    private DraftCompletionRule? StepCompletion(Step step)
    {
        if (step.Import is { } import)
            return new DraftCompletionRule("imported", false, import.Step, []);
        if (step.Ends is { } ends)
            return CompletionRule(ends);
        return step.Children.Length > 0 ? new DraftCompletionRule("children", false, null, []) : null;
    }

    private IEnumerable<WorkflowAction> StepActions(Step step) =>
        step.Actions.Concat(_definition.GlobalActions.Where(action => action.Steps.Contains(step.Name)));

    private DraftStepAction StepAction(WorkflowAction action) => new(action.Type, action.Label,
        action.Roles.Select(role => _definition.Roles.GetOrDefault(role)?.DisplayTitle ?? role).ToArray(),
        action.AllForms.Where(name => name != WorkflowAction.All).Distinct(StringComparer.Ordinal)
            .Select(name => new DraftActionForm(name, _definition.Forms.GetOrDefault(name)?.DisplayName ?? name))
            .ToArray(), action.Form == WorkflowAction.All, action.Condition is not null);

    private DraftCompletionRule CompletionRule(Condition condition)
    {
        if (condition.Name is { } name)
            return new DraftCompletionRule("named", condition.Not, name, []);
        if (condition.Part is Logical logical)
            return new DraftCompletionRule(logical.Operator == LogicalOperator.And ? "all" : "any",
                condition.Not, null, logical.Children.Select(CompletionRule).ToArray());
        if (condition.Part is EventCondition completed)
            return new DraftCompletionRule("event", condition.Not,
                _definition.Events.GetOrDefault(completed.Id)?.Label ?? completed.Id, []);
        if (condition.Part is Date date)
            return new DraftCompletionRule("date", condition.Not, date.Source, []);
        if (condition.Part is DeadlineCondition deadline)
            return new DraftCompletionRule("deadline", condition.Not, deadline.ExpressionText, []);
        if (condition.Part is Value value)
        {
            var comparison = new (string Operator, string? Operand)[]
            {
                ("equal", value.Equal), ("less", value.LessThan), ("atMost", value.LessThanOrEqual),
                ("greater", value.GreaterThan), ("atLeast", value.GreaterThanOrEqual),
                (value.IsEmpty == true ? "empty" : "notEmpty", value.IsEmpty?.ToString()), ("in", value.In),
                ("maxLength", value.MaxLength?.ToString()), ("minLength", value.MinLength?.ToString())
            }.FirstOrDefault(item => item.Operand is not null);
            return new DraftCompletionRule("value", condition.Not, value.Property, [],
                comparison.Operator, comparison.Operand);
        }

        return new DraftCompletionRule("other", condition.Not, condition.Message, []);
    }

    private IEnumerable<DraftFormStepReference> FormSteps(Form form)
    {
        foreach (var step in _definition.AllSteps)
        {
            var actions = StepActions(step)
                .Where(action => action.AllForms.Contains(form.Name)).ToArray();
            if (step.Name == form.Step || actions.Length > 0)
                yield return new DraftFormStepReference(step.Name, actions.Select(action => action.Label)
                    .OfType<BilingualString>().ToArray());
        }
    }

    public FormDto Preview(string path)
    {
        if (!_forms.TryGetValue(path, out var form))
            throw new WorkflowAuthoringException(404, "FormUnavailable", "This form is unavailable in the draft.");

        var context = ObjectContext.Create(_definition, []);
        return FormDto.Create(form, context, preview: true);
    }

    public DraftFormChange[] CompareForms(DraftWorkspace draft)
    {
        var changes = new List<DraftFormChange>();
        foreach (var path in _forms.Keys.Union(draft._forms.Keys, StringComparer.Ordinal))
        {
            var before = _forms.ContainsKey(path) ? Preview(path) : null;
            var after = draft._forms.ContainsKey(path) ? draft.Preview(path) : null;
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before, PreviewJson),
                    JsonSerializer.SerializeToElement(after, PreviewJson)))
                changes.Add(new DraftFormChange(path, before, after));
        }

        return changes.ToArray();
    }

    public (DraftWorkspace Workspace, Dictionary<string, string> Changes) Apply(
        IReadOnlyDictionary<string, string> files, IReadOnlyCollection<string> yamlFiles)
    {
        if (files.Count > 100 || files.Values.Any(string.IsNullOrWhiteSpace) ||
            yamlFiles.Any(path => path is null || !files.ContainsKey(path)))
            throw new ArgumentException("Submit up to 100 edited YAML files.");

        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        var explicitYaml = yamlFiles.ToHashSet(StringComparer.Ordinal);
        foreach (var (path, source) in files)
        {
            if (!IsEditablePath(path) || !Files.TryGetValue(path, out var original))
                throw ChangeForbidden();

            if (original == source)
                continue;

            var before = DraftYaml.ParseRoot(original);
            var after = DraftYaml.ParseRoot(source);
            if (!DraftYaml.IsUnmarked(before) || !DraftYaml.IsUnmarked(after))
                throw ChangeForbidden();

            var updated = explicitYaml.Contains(path)
                ? source
                : DraftYaml.PreserveFormatting(original, source, before, after);
            if (updated != original)
                changes.Add(path, updated);
        }

        if (changes.Count == 0)
            return (this, changes);

        var candidate = WithFiles(changes);
        CheckChanges(candidate, changes.Keys);
        return (candidate, changes);
    }

    public DraftWorkspace WithFiles(IReadOnlyDictionary<string, string> changes)
    {
        var files = new Dictionary<string, string>(Files, StringComparer.Ordinal);
        foreach (var (path, source) in changes)
            files[path] = source;
        return new DraftWorkspace(files, _project);
    }

    public void CheckChanges(DraftWorkspace next, IEnumerable<string> changedPaths)
    {
        if (_shared || next._shared ||
            (_definition.Name, _definition.InheritsFrom, _definition.IsEmbedded) !=
            (next._definition.Name, next._definition.InheritsFrom, next._definition.IsEmbedded))
            throw ChangeForbidden();

        foreach (var path in changedPaths)
        {
            if (!IsEditablePath(path) || !Files.TryGetValue(path, out var first) ||
                !next.Files.TryGetValue(path, out var second))
                throw ChangeForbidden();

            var before = DraftYaml.ParseRoot(first);
            var after = DraftYaml.ParseRoot(second);
            if (!DraftYaml.IsUnmarked(before) || !DraftYaml.IsUnmarked(after))
                throw ChangeForbidden();

            if (_forms.ContainsKey(path))
            {
                if (!SameExcept(before, after, EditableFormFields) || !PagesAllowed(before, after) ||
                    !TextAllowed(DraftYaml.Child(before, "title"), DraftYaml.Child(after, "title")) ||
                    !FormLayoutAllowed(before, after))
                    throw ChangeForbidden();
            }
            else if (!SameExcept(before, after, ["properties"]) || !PropertiesAllowed(next, before, after))
                throw ChangeForbidden();
        }
    }

    private static bool FormLayoutAllowed(YamlMappingNode before, YamlMappingNode after)
    {
        var layout = DraftYaml.Child(after, "layout");
        return Equals(DraftYaml.Child(before, "layout"), layout) || layout is null ||
               layout is YamlScalarNode { Value: { } name } &&
               FormLayouts.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool PagesAllowed(YamlMappingNode before, YamlMappingNode after)
    {
        var oldPages = Named(DraftYaml.Child(before, "pages"));
        var newPages = Named(DraftYaml.Child(after, "pages"));
        // With no explicit pages, the runtime generates a form containing all stored properties.
        if (oldPages.Count > 0 && newPages.Count == 0)
            return false;
        return oldPages.Keys.Union(newPages.Keys).All(name =>
        {
            var oldPage = oldPages.GetValueOrDefault(name);
            var newPage = newPages.GetValueOrDefault(name);
            if (Equals(oldPage, newPage))
                return true;
            return SameExcept(oldPage, newPage, EditablePageFields) &&
                   (newPage is null ||
                    TextAllowed(DraftYaml.Child(oldPage, "title"), DraftYaml.Child(newPage, "title"))) &&
                   ElementsAllowed(oldPage, newPage);
        });
    }

    private static bool ElementsAllowed(YamlMappingNode? before, YamlMappingNode? after)
    {
        var oldElements = (DraftYaml.Child(before, "elements") as YamlSequenceNode)?.Children ?? [];
        var newElements = (DraftYaml.Child(after, "elements") as YamlSequenceNode)?.Children ?? [];
        var oldCallouts = oldElements.Where(IsCallout).ToArray();
        var newCallouts = newElements.Where(IsCallout).ToArray();
        if (!CalloutsAllowed(oldCallouts, newCallouts))
            return false;

        return newElements.Where(element => !IsCallout(element)).Except(oldElements).All(element =>
            element is YamlMappingNode { Children.Count: 1 } map &&
            (DraftYaml.Child(map, "question") is YamlScalarNode ||
             DraftYaml.Child(map, "text") is { } text && TextAllowed(null, text)));

        static bool IsCallout(YamlNode node) => DraftYaml.Child(node as YamlMappingNode, "callout") is not null;
    }

    private static bool CalloutsAllowed(YamlNode[] before, YamlNode[] after)
    {
        if (before.Length != after.Length)
            return false;
        if (before.SequenceEqual(after))
            return true;

        var remaining = before.ToList();
        var edited = new List<YamlNode>();
        foreach (var callout in after)
            if (!remaining.Remove(callout))
                edited.Add(callout);

        // Each edited callout must match a distinct original, even when edits and moves happen together.
        var assignments = Enumerable.Repeat(-1, edited.Count).ToArray();
        for (var index = 0; index < edited.Count; index++)
            if (!Match(index, new bool[remaining.Count]))
                return false;
        return true;

        bool Match(int index, bool[] tried)
        {
            for (var offset = 0; offset < remaining.Count; offset++)
            {
                var original = (index + offset) % remaining.Count;
                if (tried[original] || !CalloutAllowed(remaining[original], edited[index]))
                    continue;

                tried[original] = true;
                if (assignments[original] < 0 || Match(assignments[original], tried))
                {
                    assignments[original] = index;
                    return true;
                }
            }

            return false;
        }
    }

    private static bool CalloutAllowed(YamlNode before, YamlNode after)
    {
        if (after is not YamlMappingNode { Children.Count: 1 } element ||
            DraftYaml.Child(element, "callout") is not YamlMappingNode newCallout ||
            DraftYaml.Child(before as YamlMappingNode, "callout") is not YamlMappingNode oldCallout ||
            !SameExcept(oldCallout, newCallout, EditableCalloutFields))
            return false;

        var variant = DraftYaml.Child(newCallout, "variant");
        return (variant is null || variant is YamlScalarNode { Value: { } name } &&
                   CalloutVariants.Contains(name, StringComparer.OrdinalIgnoreCase)) &&
               TextAllowed(DraftYaml.Child(oldCallout, "title"), DraftYaml.Child(newCallout, "title")) &&
               TextAllowed(DraftYaml.Child(oldCallout, "text"), DraftYaml.Child(newCallout, "text"));
    }

    private bool PropertiesAllowed(DraftWorkspace next, YamlMappingNode before, YamlMappingNode after)
    {
        var oldProperties = Named(DraftYaml.Child(before, "properties"), allowMapping: true);
        var newProperties = Named(DraftYaml.Child(after, "properties"), allowMapping: true);
        var oldQuestions = QuestionNames();
        var newQuestions = next.QuestionNames();
        return oldProperties.Keys.Union(newProperties.Keys).All(name =>
        {
            var oldProperty = oldProperties.GetValueOrDefault(name);
            var newProperty = newProperties.GetValueOrDefault(name);
            if (Equals(oldProperty, newProperty))
                return true;
            if (!oldQuestions.Contains(name) && !newQuestions.Contains(name))
                return false;

            if (!SameExcept(oldProperty, newProperty, EditablePropertyFields))
                return false;
            if ((oldProperty is not null &&
                 _definition.Properties.GetOrDefault(name) is ReferencePropertyDefinition
                     or ObjectPropertyDefinition) ||
                (newProperty is not null &&
                 next._definition.Properties.GetOrDefault(name) is ReferencePropertyDefinition
                     or ObjectPropertyDefinition))
                return false;

            if (newProperty is null)
                return true;

            var settings = new YamlMappingNode(newProperty.Children.Where(field =>
                field.Key is YamlScalarNode { Value: { } key } && EditablePropertyFields.Contains(key)));
            if (DraftYaml.Name(settings) is null)
                settings.Add("name", name);
            return DraftQuestionSettings.Read(settings, name) is not null;
        });
    }

    private HashSet<string> QuestionNames() => _forms.Values.SelectMany(form => form.Pages)
        .SelectMany(page => page.Questions).Select(question => question.Name).ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, YamlMappingNode> Named(YamlNode? node, bool allowMapping = false)
    {
        if (node is null)
            return new Dictionary<string, YamlMappingNode>();
        if (node is YamlMappingNode map && allowMapping)
            return map.Children.ToDictionary(entry => ((YamlScalarNode)entry.Key).Value!,
                entry => (YamlMappingNode)entry.Value, StringComparer.Ordinal);
        if (node is YamlSequenceNode list)
            return list.Children.Cast<YamlMappingNode>().ToDictionary(entry => DraftYaml.Name(entry) ??
                                                                               throw new ArgumentException(
                                                                                   "Each page and property must have a name."),
                StringComparer.Ordinal);
        throw new ArgumentException("Expected named YAML entries.");
    }

    private static bool SameExcept(YamlMappingNode? before, YamlMappingNode? after, string[] editable)
        => Unchanged(before, after, editable) && Unchanged(after, before, editable);

    private static bool Unchanged(YamlMappingNode? from, YamlMappingNode? to, string[] editable)
        => from is null || from.Children.All(entry => entry.Key is YamlScalarNode { Value: { } name } &&
                                                      (editable.Contains(name) || to is not null &&
                                                          to.Children.TryGetValue(entry.Key, out var value) &&
                                                          entry.Value.Equals(value)));

    private static bool TextAllowed(YamlNode? before, YamlNode? after)
        => Equals(before, after) || after switch
        {
            null => true,
            YamlScalarNode { Value: { } text } => !text.Contains("{{", StringComparison.Ordinal),
            YamlMappingNode map => map.Children.All(entry => entry.Key is YamlScalarNode { Value: "en" or "nl" } &&
                                                             TextAllowed(
                                                                 DraftYaml.Child(before as YamlMappingNode,
                                                                     ((YamlScalarNode)entry.Key).Value!), entry.Value)),
            _ => false
        };

    private bool IsEditablePath(string path) => !_shared &&
                                                (_forms.ContainsKey(path) || IsPropertyFile(path, _project.Folder));

    private static bool IsFormFile(string path, string folder) => IsDirectYaml(path, folder + "/Forms");

    private static bool IsPropertyFile(string path, string folder) =>
        IsDirectYaml(path, folder) || IsDirectYaml(path, folder + "/Steps");

    private static bool IsDirectYaml(string path, string folder) =>
        path.StartsWith(folder + "/", StringComparison.Ordinal) && YamlFilename().IsMatch(path[(folder.Length + 1)..]);

    [GeneratedRegex(@"\A[a-zA-Z0-9][a-zA-Z0-9_.-]*\.yaml\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YamlFilename();

    private static string Folder(WorkflowDefinition definition) => definition.SourceFolder.Replace('\\', '/');

    private static WorkflowAuthoringException ChangeForbidden()
        => new(403, "DraftChangesForbidden", "This draft contains changes outside the permitted form content.");
}