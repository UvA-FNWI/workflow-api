using Serilog;
using UvA.Workflow.WorkflowModel.Conditions;
using YamlDotNet.Core;
using YamlDotNet.Serialization.NamingConventions;
using Path = System.IO.Path;

namespace UvA.Workflow.WorkflowModel;

public partial class ModelParser
{
    private readonly IContentProvider _contentProvider;

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public List<Service> Services { get; }
    public List<Role> Roles { get; }
    public Dictionary<string, WorkflowDefinition> WorkflowDefinitions { get; } = new();
    private List<ValueSet> ValueSets { get; }
    private List<Condition> NamedConditions { get; }

    public ModelParser(IContentProvider contentProvider)
    {
        _contentProvider = contentProvider;
        Roles = Read<Role>();
        Services = Read<Service>();
        ValidateServices(Services);
        ValueSets = Read<ValueSet>();
        NamedConditions = Read<Condition>();

        var definitions = GetWorkflowDefinitionFolders()
            .Select(folder =>
            {
                var definition = Parse<WorkflowDefinition>(Path.Combine(folder, "Entity.yaml"));
                if (definition == null)
                    throw new Exception($"No valid Entity.yaml in folder {folder}");
                definition.SourceFolder = folder;
                return definition;
            })
            .OrderBy(e => e.InheritsFrom != null);

        foreach (var definition in definitions)
        {
            Log.Debug("Processing definition {Name}", definition.Name);
            foreach (var file in contentProvider.GetFiles(definition.SourceFolder)
                         .Where(f => Path.GetFileName(f) != "Entity.yaml"))
            {
                var content = Parse<WorkflowDefinition>(file);
                foreach (var prop in content.Properties)
                {
                    if (definition.Properties.Contains(prop.Name))
                        throw new Exception(
                            $"Definition '{definition.Name}' defines property '{prop.Name}' in multiple files.");
                    definition.Properties.Add(prop);
                }

                if (content.Resources.Length > 0)
                {
                    if (definition.Resources.Length > 0)
                        throw new Exception(
                            $"Definition '{definition.Name}' defines resources in multiple files.");
                    definition.Resources = content.Resources;
                }

                if (content.GlobalActions.Count == 0) continue;
                if (definition.GlobalActions.Count > 0)
                    throw new Exception(
                        $"Definition '{definition.Name}' defines globalActions in multiple files.");
                definition.GlobalActions = content.GlobalActions;
            }

            definition.Forms = Read<Form>(definition.SourceFolder);
            definition.Screens = Read<Screen>(definition.SourceFolder);
            definition.AllSteps = Read<Step>(definition.SourceFolder);
            definition.Emails = Read<TemplateMessage>(definition.SourceFolder);

            foreach (var entry in Read<Condition>(definition.SourceFolder))
                NamedConditions.Add(entry);

            if (definition.InheritsFrom != null)
                ApplyInheritance(definition, WorkflowDefinitions[definition.InheritsFrom]);

            definition.Steps = definition.StepNames.Select(n => definition.AllSteps.Get(n)).ToList();
            WorkflowDefinitions[definition.Name] = definition;

            foreach (var prop in definition.Properties.Where(p => p.UnderlyingType == "User"))
                if (!Roles.Contains(prop.Name))
                    Roles.Add(new Role { Name = prop.Name });

            foreach (var val in definition.Events)
                val.Name = val.Name;

            foreach (var action in definition.GlobalActions.Except(definition.Parent?.GlobalActions ?? []))
            {
                action.WorkflowDefinition = definition.Name;
                foreach (var role in action.Roles)
                    Roles.Get(role).Actions.Add(action);
            }

            foreach (var step in
                     definition.AllSteps.Except((IEnumerable<Step>?)definition.Parent?.AllSteps ?? []))
            {
                step.Children = step.ChildNames.Select(s => definition.AllSteps.Get(s)).ToArray();
                foreach (var action in step.Actions)
                {
                    action.WorkflowDefinition = definition.Name;
                    action.Steps = [step.Name];
                    foreach (var role in action.Roles)
                    {
                        var roleObject = Roles.GetOrDefault(role);
                        if (roleObject == null)
                            throw new Exception($"Role {role} is used in action {action.Name} but does not exist");
                        roleObject.Actions.Add(action);
                    }
                }

                foreach (var prop in step.Properties)
                    definition.Properties.Add(prop);
            }
        }

        Roles.ForEach(PreProcess);
        ValueSets.ForEach(PreProcess);
        WorkflowDefinitions.Values.ForEach(PreProcess);
    }

    private IEnumerable<string> GetWorkflowDefinitionFolders(string? root = null)
    {
        foreach (var folder in _contentProvider.GetFolders(root))
        {
            if (Path.GetFileName(folder) == "Common")
                continue;

            if (_contentProvider.GetFiles(folder)
                .Any(file => string.Equals(Path.GetFileName(file), "Entity.yaml", StringComparison.OrdinalIgnoreCase)))
            {
                yield return folder;
                continue;
            }

            foreach (var child in GetWorkflowDefinitionFolders(folder))
                yield return child;
        }
    }

    private void PreProcess(Role role)
    {
        role.Actions = role.Actions.Union(role.InheritFrom.SelectMany(r => Roles.Get(r).Actions)).ToList();
        foreach (var act in role.Actions)
        {
            PreProcess(act.OnAction);

            if (act.Form is not (null or Action.All) && act.WorkflowDefinition != null &&
                !WorkflowDefinitions[act.WorkflowDefinition].Forms.Contains(act.Form))
                throw new Exception($"{role.Name}: form {act.Form} not found for entity {act.WorkflowDefinition}");
        }
    }

    private void PreProcess(ValueSet set)
    {
        foreach (var entry in set.Values)
            PreProcess(entry);

        ValidateSorting(set);
    }

    private static readonly Dictionary<ChoiceSortField, Func<Choice, object?>> ChoiceFieldSelectors = new()
    {
        [ChoiceSortField.Name] = c => c.Name,
        [ChoiceSortField.Text] = c => c.Text,
        [ChoiceSortField.Value] = c => c.Value,
        [ChoiceSortField.Description] = c => c.Description
    };

    private static void ValidateSorting(ValueSet set)
    {
        if (set.Sorting == null)
            return;

        var selector = ChoiceFieldSelectors[set.Sorting.Field];
        var missing = set.Values.Where(v => selector(v) == null).Select(v => v.Name).ToList();
        if (missing.Count > 0)
            throw new Exception(
                $"ValueSet '{set.Name}': cannot sort on field '{set.Sorting.Field}' because it is not present on all values. " +
                $"Missing for: {string.Join(", ", missing)}");
    }

    private void PreProcess(Form form, WorkflowDefinition workflowDefinition)
    {
        form.WorkflowDefinition = workflowDefinition;

        if (form.SubmittedWhenEvents is { Length: 0 })
            throw new Exception($"Form {form.Name} has an empty submittedWhenEvents list");

        if (!form.Pages.Any() && form.TargetFormName == null)
            form.Pages.Add(new Page
            {
                FieldNames = workflowDefinition.Properties.Select(v => v.Name).ToArray()
            });

        foreach (var ent in form.Pages)
        {
            var missingFields = ent.FieldNames.Where(f => workflowDefinition.Properties.GetOrDefault(f) == null)
                .ToArray();
            if (missingFields.Any())
                throw new Exception(
                    $"Form {form.Name} references unknown property {missingFields.ToSeparatedString()}");
            ent.Fields = ent.FieldNames.Select(q => workflowDefinition.Properties.Get(q)).ToArray();
        }

        workflowDefinition.Events.Add(new() { Name = form.Name });
        EnsureEffectEventsExist(form.OnSubmit, workflowDefinition);
        EnsureEffectEventsExist(form.OnSave, workflowDefinition);

        if (form is { PropertyName: not null, TargetFormName: not null })
            form.TargetForm = WorkflowDefinitions[workflowDefinition.Properties.Get(form.PropertyName).UnderlyingType]
                .Forms.Get(form.TargetFormName);

        PreProcess(form.OnSubmit);
        PreProcess(form.OnSave);
    }

    private void PreProcess(Effect[] effects)
    {
        foreach (var effect in effects)
        {
            if (effect == null)
                throw new Exception("Empty effect found");
            PreProcess(effect.Condition);
        }
    }

    private void PreProcess(Resource resource, WorkflowDefinition workflowDefinition)
    {
        switch (resource.Type)
        {
            case ResourceLayout.Text:
                if (resource.Content == null ||
                    (string.IsNullOrWhiteSpace(resource.Content.En) && string.IsNullOrWhiteSpace(resource.Content.Nl)))
                    throw new Exception(
                        $"Resource '{resource.Name}' in '{workflowDefinition.Name}' has type 'Text' but no content is set.");
                if (resource.Items?.Length > 0)
                    throw new Exception(
                        $"Resource '{resource.Name}' in '{workflowDefinition.Name}' has type 'Text' but also defines items. Remove the items or change the type to 'Links'.");
                break;

            case ResourceLayout.Links:
                if (resource.Items?.Length == 0)
                    throw new Exception(
                        $"Resource '{resource.Name}' in '{workflowDefinition.Name}' has type 'Links' but contains no items.");
                var invalidItems = resource.Items?
                    .Where(i => i.Type != ResourceType.Link && i.Type != ResourceType.Download)
                    .Select(i => $"'{i.Name}' (type: {i.Type})")
                    .ToList() ?? [];
                if (invalidItems.Count > 0)
                    throw new Exception(
                        $"Resource '{resource.Name}' in '{workflowDefinition.Name}' has items with invalid types for a 'Links' resource: " +
                        $"{string.Join(", ", invalidItems)}. Only 'Link' and 'Download' are allowed.");
                break;
        }
    }

    private void PreProcess(WorkflowDefinition workflowDefinition)
    {
        foreach (var ent in workflowDefinition.Properties)
        {
            ent.ParentType = workflowDefinition;
            ent.Name = ent.Name;
            PreProcess(ent);
        }

        foreach (var form in workflowDefinition.Forms)
            PreProcess(form, workflowDefinition);
        foreach (var screen in workflowDefinition.Screens)
            PreProcess(screen, workflowDefinition);
        foreach (var step in workflowDefinition.Steps)
            PreProcess(step, workflowDefinition);
        foreach (var field in workflowDefinition.Fields)
            PreProcess(field, workflowDefinition);
        foreach (var relatedUser in workflowDefinition.RelatedUsers)
            PreProcess(relatedUser, workflowDefinition);
        foreach (var resource in workflowDefinition.Resources)
            PreProcess(resource, workflowDefinition);
        foreach (var form in workflowDefinition.Forms)
            ValidateSubmittedWhenEvents(form, workflowDefinition);

        workflowDefinition.ModelParser = this;
    }

    private static void EnsureEffectEventsExist(IEnumerable<Effect> effects, WorkflowDefinition workflowDefinition)
    {
        foreach (var eventId in effects
                     .SelectMany(effect => new[] { effect?.Event, effect?.UndoEvent })
                     .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
                     .Cast<string>())
        {
            if (workflowDefinition.Events.All(e => e.Name != eventId))
                workflowDefinition.Events.Add(new EventDefinition { Name = eventId });
        }
    }

    private static void ValidateSubmittedWhenEvents(Form form, WorkflowDefinition workflowDefinition)
    {
        if (form.SubmittedWhenEvents == null)
            return;

        var unknownEvents = form.SubmittedWhenEvents
            .Where(string.IsNullOrWhiteSpace)
            .Concat(form.SubmittedWhenEvents.Where(eventId => workflowDefinition.Events.All(e => e.Name != eventId)))
            .Distinct()
            .ToArray();

        if (unknownEvents.Any())
            throw new Exception(
                $"Form {form.Name} references unknown submittedWhenEvents event {unknownEvents.ToSeparatedString()}");
    }

    private void PreProcess(Step step, WorkflowDefinition workflowDefinition)
    {
        PreProcess(step.Condition);
        PreProcess(step.Ends);

        foreach (var ev in step.Events)
        {
            var existing = workflowDefinition.Events.Find(e => e.Name == ev.Name);
            if (existing != null)
            {
                if (existing.Suppresses != null && ev.Suppresses != null)
                    throw new InvalidOperationException(
                        $"Event '{ev.Name}' in workflow '{workflowDefinition.Name}' already has a suppresses value defined.");
                if (ev.Suppresses != null)
                    existing.Suppresses = ev.Suppresses;
            }
            else
            {
                workflowDefinition.Events.Add(ev);
            }
        }

        foreach (var ev in step.Actions.SelectMany(a => a.OnAction.Select(t => t.Event)).Where(t => t != null))
            if (workflowDefinition.Events.All(e => e.Name != ev!))
                workflowDefinition.Events.Add(new EventDefinition { Name = ev! });

        foreach (var child in step.Children)
        {
            child.ParentStep = step;
            PreProcess(child, workflowDefinition);
        }
    }

    private void PreProcess(Field field, WorkflowDefinition workflowDefinition)
    {
        if (field.Property != null)
            field.PropertyDefinition = workflowDefinition.Properties.GetOrDefault(field.Property);
    }

    private void PreProcess(RelatedUser relatedUser, WorkflowDefinition workflowDefinition)
    {
        relatedUser.PropertyDefinition = ResolvePropertyDefinition(workflowDefinition, relatedUser.Property);
    }

    private static PropertyDefinition? ResolvePropertyDefinition(WorkflowDefinition workflowDefinition,
        string propertyPath)
    {
        var type = workflowDefinition;
        var parts = propertyPath.Split('.');
        PropertyDefinition? property = null;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            property = type.Properties.GetOrDefault(part);
            if (property == null)
                return null;

            if (i < parts.Length - 1)
            {
                if (property.WorkflowDefinition == null)
                    return null;

                type = property.WorkflowDefinition;
            }
        }

        return property;
    }

    private void PreProcess(Screen screen, WorkflowDefinition workflowDefinition)
    {
        foreach (var col in screen.Columns)
        {
            if (col.Property != null)
            {
                col.PropertyDefinition = workflowDefinition.Properties.GetOrDefault(col.Property.Split('.')[0]);
                if (col.Property.EndsWith("Event"))
                    col.Event = workflowDefinition.Events.FirstOrDefault(e => e.Name == col.Property[..^5]);
            }
        }
    }

    private PropertyDefinition PreProcess(PropertyDefinition propertyDefinition)
    {
        propertyDefinition.Layout = NormalizeLayout(propertyDefinition.Layout);

        foreach (var entry in propertyDefinition.Values ?? [])
            PreProcess(entry);

        if (ValueSets.TryGetValue(propertyDefinition.UnderlyingType, out var set))
        {
            propertyDefinition.Values = set.Values;
            propertyDefinition.Sorting = set.Sorting;
        }

        if (WorkflowDefinitions.TryGetValue(propertyDefinition.UnderlyingType, out var type))
            propertyDefinition.WorkflowDefinition = type;

        if (propertyDefinition.Rubric != null)
            PreProcess(propertyDefinition.Rubric, propertyDefinition);
        PreProcess(propertyDefinition.Condition);
        PreProcess(propertyDefinition.OnSave);

        foreach (var dep in propertyDefinition.Conditions
                     .SelectMany(c => c.Part.Dependants)
                     .SelectMany(d => d.ToString().Split('.'))
                     .Distinct())
            propertyDefinition.ParentType.Properties.GetOrDefault(dep)?.DependentQuestions
                .Add(propertyDefinition);

        if (propertyDefinition.LinkedTo != null &&
            propertyDefinition.ParentType.Properties.GetOrDefault(propertyDefinition.LinkedTo) == null)
            throw new Exception(
                $"Property '{propertyDefinition.Name}' in '{propertyDefinition.ParentType.Name}' has linkedTo '{propertyDefinition.LinkedTo}', but that property does not exist.");

        try
        {
            _ = propertyDefinition.DataType;
        }
        catch (Exception)
        {
            throw new Exception($"Invalid data type {propertyDefinition.Type} for property {propertyDefinition.Name}");
        }

        return propertyDefinition;
    }

    private static Dictionary<string, object>? NormalizeLayout(Dictionary<string, object>? layout)
    {
        if (layout == null)
            return null;

        return layout.ToDictionary(entry => entry.Key, entry => NormalizeLayoutValue(entry.Value));
    }

    private static object NormalizeLayoutValue(object value)
        => value switch
        {
            Dictionary<string, object> dict => NormalizeLayout(dict)!,
            List<object> list => list.Select(NormalizeLayoutValue).ToList(),
            string text when bool.TryParse(text, out var boolean) => boolean,
            _ => value
        };

    private void PreProcess(Condition? condition)
    {
        if (condition == null) return;
        if (condition.Name is not null)
        {
            condition.NamedCondition = NamedConditions.FirstOrDefault(c => c.Name == condition.Name);
            if (condition.NamedCondition == null)
                throw new Exception($"Condition {condition.Name} not found");
        }

        condition.Logical?.Children.ForEach(PreProcess);
    }

    private void PreProcess(Choice choice)
    {
        PreProcess(choice.Condition);
    }

    private void PreProcess(List<RubricEntry> rubric, PropertyDefinition propertyDefinition)
    {
        if (propertyDefinition.Values == null)
            throw new Exception(
                $"Property '{propertyDefinition.Name}' has rubric entries defined but no values. Rubrics can only be used on properties with predefined values.");

        var layoutType = propertyDefinition.Layout?.GetValueOrDefault("type")?.ToString();

        if (layoutType == "Rubric" && rubric == null)
            throw new Exception(
                $"Property '{propertyDefinition.Name}' has layout type 'Rubric' but no rubric entries defined.");

        if (propertyDefinition.Rubric != null && layoutType != "Rubric")
            throw new Exception(
                $"Property '{propertyDefinition.Name}' has rubric entries but layout type is '{layoutType ?? "not set"}'. Set layout type to 'Rubric'.");

        var validNames = propertyDefinition.Values.Select(v => v.Name).ToHashSet();
        var unknownGrades = rubric
            .SelectMany(e => e.Grades
                .Where(g => !validNames.Contains(g))
                .Select(g => $"'{g}' in entry '{e.Name}'"))
            .ToList();

        if (unknownGrades.Count > 0)
            throw new Exception(
                $"Property '{propertyDefinition.Name}' has rubric grades not found in type '{propertyDefinition.UnderlyingType}': " +
                $"{string.Join(", ", unknownGrades)}. Valid options are: {string.Join(", ", validNames)}");

        var duplicateGrades = rubric
            .SelectMany(e => e.Grades.Select(g => (Grade: g, Entry: e.Name)))
            .GroupBy(g => g.Grade)
            .Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}' in entries {string.Join(", ", g.Select(x => $"'{x.Entry}'"))}")
            .ToList();

        if (duplicateGrades.Count > 0)
            throw new Exception(
                $"Property '{propertyDefinition.Name}' has rubric grades assigned to multiple entries: " +
                $"{string.Join(", ", duplicateGrades)}");
    }

    private static void ValidateServices(List<Service> services)
    {
        foreach (var service in services)
        foreach (var operation in service.Operations)
        foreach (var output in operation.Outputs)
        {
            if (output.Path != null && output.Template != null)
                throw new Exception(
                    $"ServiceOutput '{output.Name}' in '{service.Name}.{operation.Name}' has both 'path' and 'template' set — use one or the other.");
            if (output.Path == null && output.Template == null)
                throw new Exception(
                    $"ServiceOutput '{output.Name}' in '{service.Name}.{operation.Name}' requires either 'path' or 'template'.");
        }
    }

    private T Parse<T>(string file)
    {
        try
        {
            Log.Debug("Parsing {File} for {Type}", file, typeof(T).Name);
            var obj = _deserializer.Deserialize<T>(_contentProvider.GetFile(file));
            return obj;
        }
        catch (YamlException ex)
        {
            throw new Exception($"Failed to parse {file}:{ex.Start.Line}:{ex.Start.Column}. {ex.Message}");
        }
    }

    private List<T> Read<T>(string? root = null)
    {
        Log.Debug("Reading {Type} from {Root}", typeof(T).Name, root);
        root ??= "Common";

        var typeName = typeof(T).Name;
        var folder = typeName switch
        {
            nameof(TemplateMessage) => "Emails",
            _ when typeName.StartsWith("Variant") => typeName.Replace("Variant", "") + "s",
            _ => typeName + "s"
        };
        var result = new List<T>();
        var nameProperty = typeof(T).GetProperty("Name");
        foreach (var filePath in _contentProvider.GetFiles($"{root}/{folder}"))
        {
            var obj = Parse<T>(filePath);
            if (obj == null)
                throw new Exception($"Invalid file: {filePath}");

            // Set the entity name to the filename when the name is not defined in the file
            if (nameProperty?.PropertyType == typeof(string) && nameProperty.GetValue(obj)?.ToString() == null)
                nameProperty.SetValue(obj, Path.GetFileNameWithoutExtension(filePath));

            result.Add(obj);
        }

        return result;
    }
}