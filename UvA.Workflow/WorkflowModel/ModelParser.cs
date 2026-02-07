using Serilog;
using UvA.Workflow.WorkflowModel;
using YamlDotNet.Core;
using YamlDotNet.Serialization.NamingConventions;
using Path = System.IO.Path;

namespace UvA.Workflow.Entities.Domain;

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
        ValueSets = Read<ValueSet>();
        NamedConditions = Read<Condition>();

        var definitions = _contentProvider.GetFolders()
            .Where(d => Path.GetFileName(d) != "Common")
            .Select(d => Parse<WorkflowDefinition>(Path.Combine(d, "Entity.yaml")))
            .OrderBy(e => e.InheritsFrom != null);

        foreach (var definition in definitions)
        {
            Log.Debug("Processing definition {Name}", definition.Name);
            foreach (var file in contentProvider.GetFiles(definition.Name)
                         .Where(f => Path.GetFileNameWithoutExtension(f) != "Entity.yaml"))
            {
                var content = Parse<WorkflowDefinition>(file);
                if (content.Properties.Count > 0) definition.Properties = content.Properties;
                if (content.GlobalActions.Count > 0) definition.GlobalActions = content.GlobalActions;
            }

            definition.Forms = Read<Form>(definition.Name);
            definition.Screens = Read<Screen>(definition.Name);
            definition.AllSteps = Read<Step>(definition.Name);

            foreach (var entry in Read<Condition>(definition.Name))
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
    }

    private void PreProcess(Form form, WorkflowDefinition workflowDefinition)
    {
        form.WorkflowDefinition = workflowDefinition;

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

        if (form is { PropertyName: not null, TargetFormName: not null })
            form.TargetForm = WorkflowDefinitions[workflowDefinition.Properties.Get(form.PropertyName).UnderlyingType]
                .Forms.Get(form.TargetFormName);

        if (form.PropertyDefinitions.GroupBy(q => q.Name).Any(g => g.Count() > 1))
            throw new Exception($"Form {form.Name} has multiple questions with the same name");

        PreProcess(form.OnSubmit);
        PreProcess(form.OnSave);
    }

    private void PreProcess(Effect[] effects)
    {
        foreach (var effect in effects)
            PreProcess(effect.Condition);
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
        foreach (var field in workflowDefinition.HeaderFields)
            PreProcess(field, workflowDefinition);

        workflowDefinition.ModelParser = this;
    }

    private void PreProcess(Step step, WorkflowDefinition workflowDefinition)
    {
        PreProcess(step.Condition);
        PreProcess(step.Ends);
        foreach (var ev in step.Actions.SelectMany(a => a.OnAction.Select(t => t.Event)).Where(t => t != null))
            if (workflowDefinition.Events.All(e => e.Name != ev!))
                workflowDefinition.Events.Add(new EventDefinition { Name = ev! });
        foreach (var child in step.Children)
            PreProcess(child, workflowDefinition);
    }

    private void PreProcess(Field field, WorkflowDefinition workflowDefinition)
    {
        if (field.Property != null)
            field.PropertyDefinition = workflowDefinition.Properties.GetOrDefault(field.Property);
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
        foreach (var entry in propertyDefinition.Values ?? [])
            PreProcess(entry);

        if (ValueSets.TryGetValue(propertyDefinition.UnderlyingType, out var set))
            propertyDefinition.Values = set.Values;
        if (WorkflowDefinitions.TryGetValue(propertyDefinition.UnderlyingType, out var type))
            propertyDefinition.WorkflowDefinition = type;
        PreProcess(propertyDefinition.Condition);
        PreProcess(propertyDefinition.OnSave);

        foreach (var dep in propertyDefinition.Conditions
                     .SelectMany(c => c.Part.Dependants)
                     .SelectMany(d => d.ToString().Split('.'))
                     .Distinct())
            propertyDefinition.ParentType.Properties.GetOrDefault(dep)?.DependentQuestions
                .Add(propertyDefinition);

        return propertyDefinition;
    }

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

            // If the object has a name property, set it to the entity name
            if (nameProperty?.PropertyType == typeof(string))
            {
                var entityName = Path.GetFileNameWithoutExtension(filePath);
                nameProperty.SetValue(obj, entityName);
            }

            result.Add(obj);
        }

        return result;
    }
}