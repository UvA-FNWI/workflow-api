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

    public Dictionary<string, Role> Roles { get; }
    public Dictionary<string, WorkflowDefinition> WorkflowDefinitions { get; } = new();
    private Dictionary<string, ValueSet> ValueSets { get; }
    private Dictionary<string, Condition> NamedConditions { get; }

    public ModelParser(IContentProvider contentProvider)
    {
        _contentProvider = contentProvider;
        Roles = Read<Role>();
        ValueSets = Read<ValueSet>();
        NamedConditions = Read<Condition>();

        var definitions = _contentProvider.GetFolders()
            .Where(d => Path.GetFileName(d) != "Common")
            .Select(d => Parse<WorkflowDefinition>(Path.Combine(d, "Entity.yaml")))
            .OrderBy(e => e.InheritsFrom != null);

        foreach (var definition in definitions)
        {
            foreach (var file in contentProvider.GetFiles(definition.Name)
                         .Where(f => Path.GetFileNameWithoutExtension(f) != "Entity.yaml"))
            {
                var content = Parse<WorkflowDefinition>(file);
                if (content.Properties.Count > 0) definition.Properties = content.Properties;
                if (content.Actions.Count > 0) definition.Actions = content.Actions;
            }

            definition.Forms = Read<Form>(definition.Name);
            definition.Screens = Read<Screen>(definition.Name);
            definition.AllSteps = Read<Step>(definition.Name);

            foreach (var entry in Read<Condition>(definition.Name))
                NamedConditions.Add(entry.Key, entry.Value);

            if (definition.InheritsFrom != null)
                ApplyInheritance(definition, WorkflowDefinitions[definition.InheritsFrom]);

            definition.Steps = definition.StepNames.Select(n => definition.AllSteps[n]).ToList();
            WorkflowDefinitions[definition.Name] = definition;

            foreach (var prop in definition.Properties.Where(p => p.Value.UnderlyingType == "User"))
                if (!Roles.ContainsKey(prop.Key))
                    Roles[prop.Key] = new Role { Name = prop.Key };

            foreach (var val in definition.Events)
                val.Value.Name = val.Key;

            foreach (var action in definition.Actions.Except(definition.Parent?.Actions ?? []))
            {
                action.WorkflowDefinition = definition.Name;
                foreach (var role in action.Roles)
                    Roles[role].Actions.Add(action);
            }

            foreach (var step in
                     definition.AllSteps.Values.Except((IEnumerable<Step>?)definition.Parent?.AllSteps.Values ?? []))
            {
                step.Children = step.ChildNames.Select(s => definition.AllSteps[s]).ToArray();
                foreach (var action in step.Actions)
                {
                    action.WorkflowDefinition = definition.Name;
                    action.Steps = [step.Name];
                    foreach (var role in action.Roles)
                        Roles[role].Actions.Add(action);
                }

                foreach (var prop in step.Properties)
                    definition.Properties.Add(prop.Key, prop.Value);
            }
        }

        Roles.Values.ForEach(PreProcess);
        ValueSets.Values.ForEach(PreProcess);
        WorkflowDefinitions.Values.ForEach(PreProcess);
    }

    private void PreProcess(Role role)
    {
        role.Actions = role.Actions.Union(role.InheritFrom.SelectMany(r => Roles[r].Actions)).ToList();
        foreach (var act in role.Actions)
        {
            PreProcess(act.OnAction);

            if (act.Form != null && act.WorkflowDefinition != null &&
                !WorkflowDefinitions[act.WorkflowDefinition].Forms.ContainsKey(act.Form))
                throw new Exception($"{role.Name}: form {act.Form} not found for entity {act.WorkflowDefinition}");
        }
    }

    private void PreProcess(ValueSet set)
    {
        foreach (var entry in set.Values)
        {
            entry.Value.Name = entry.Key;
            PreProcess(entry.Value);
        }
    }

    private void PreProcess(Form form, WorkflowDefinition workflowDefinition)
    {
        form.WorkflowDefinition = workflowDefinition;

        if (!form.Pages.Any() && form.TargetFormName == null)
            form.Pages["Default"] = new Page
            {
                FieldNames = workflowDefinition.Properties.Values.Select(v => v.Name).ToArray()
            };

        foreach (var ent in form.Pages)
        {
            ent.Value.Name = ent.Key;
            ent.Value.Fields = ent.Value.FieldNames.Select(q => workflowDefinition.Properties[q]).ToArray();
        }

        workflowDefinition.Events.Add(form.Name, new() { Name = form.Name });

        if (form is { Property: not null, TargetFormName: not null })
            form.TargetForm = WorkflowDefinitions[workflowDefinition.Properties[form.Property].UnderlyingType]
                .Forms[form.TargetFormName];

        if (form.PropertyDefinitions.GroupBy(q => q.Name).Any(g => g.Count() > 1))
            throw new Exception($"Form {form.Name} has multiple questions with the same name");

        PreProcess(form.OnSubmit);
        PreProcess(form.OnSave);
    }

    private void PreProcess(Trigger[] triggers)
    {
        foreach (var trigger in triggers)
            PreProcess(trigger.Condition);
    }

    private void PreProcess(WorkflowDefinition workflowDefinition)
    {
        foreach (var ent in workflowDefinition.Properties)
        {
            ent.Value.ParentType = workflowDefinition;
            ent.Value.Name = ent.Key;
            PreProcess(ent.Value);
        }

        foreach (var form in workflowDefinition.Forms.Values)
            PreProcess(form, workflowDefinition);
        foreach (var screen in workflowDefinition.Screens.Values)
            PreProcess(screen, workflowDefinition);
        foreach (var step in workflowDefinition.Steps)
            PreProcess(step, workflowDefinition);

        workflowDefinition.ModelParser = this;
    }

    private void PreProcess(Step step, WorkflowDefinition workflowDefinition)
    {
        PreProcess(step.Condition);
        PreProcess(step.Ends);
        foreach (var ev in step.Actions.SelectMany(a => a.OnAction.Select(t => t.Event)).Where(t => t != null))
            if (!workflowDefinition.Events.ContainsKey(ev!))
                workflowDefinition.Events.Add(ev!, new() { Name = ev! });
    }

    private void PreProcess(Screen screen, WorkflowDefinition workflowDefinition)
    {
        foreach (var col in screen.Columns)
        {
            if (col.Property != null)
            {
                col.Question = workflowDefinition.Properties.GetValueOrDefault(col.Property.Split('.')[0]);
                if (col.Property.EndsWith("Event"))
                    col.Event = workflowDefinition.Events.GetValueOrDefault(col.Property[..^5]);
            }
        }
    }

    private PropertyDefinition PreProcess(PropertyDefinition propertyDefinition)
    {
        foreach (var entry in propertyDefinition.Values ?? [])
        {
            entry.Value.Name = entry.Key;
            PreProcess(entry.Value);
        }

        if (ValueSets.TryGetValue(propertyDefinition.UnderlyingType, out var set))
            propertyDefinition.Values = set.Values;
        if (WorkflowDefinitions.TryGetValue(propertyDefinition.UnderlyingType, out var type))
            propertyDefinition.WorkflowDefinition = type;
        PreProcess(propertyDefinition.Condition);
        PreProcess(propertyDefinition.OnSave);
        if (propertyDefinition.Table != null)
            propertyDefinition.Table.Form = propertyDefinition.ParentType.Forms[propertyDefinition.Table.FormReference];

        foreach (var dep in propertyDefinition.Conditions.SelectMany(c => c.Part.Dependants).Distinct())
        {
            var depName = dep.ToString().Split('.').Last();
            propertyDefinition.ParentType.Properties.GetValueOrDefault(depName)?.DependentQuestions
                .Add(propertyDefinition);
        }

        return propertyDefinition;
    }

    private void PreProcess(Condition? condition)
    {
        if (condition == null) return;
        if (condition.Name != null)
            condition.NamedCondition = NamedConditions[condition.Name];
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
            return _deserializer.Deserialize<T>(_contentProvider.GetFile(file));
        }
        catch (YamlException ex)
        {
            throw new Exception($"Failed to parse {file}:{ex.Start.Line}:{ex.Start.Column}. {ex.Message}");
        }
    }


    private Dictionary<string, T> Read<T>(string? root = null)
    {
        root ??= "Common";
        var name = typeof(T).Name;
        var folder = name switch
        {
            _ when name.StartsWith("Variant") => name.Replace("Variant", "") + "s",
            _ => name + "s"
        };
        return _contentProvider.GetFiles($"{root}/{folder}")
            .ToDictionary(
                f => Path.GetFileNameWithoutExtension(f),
                f => Parse<T>(f)
            );
    }
}