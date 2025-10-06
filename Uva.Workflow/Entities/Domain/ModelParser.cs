using YamlDotNet.Serialization.NamingConventions;
using Path = System.IO.Path;

namespace Uva.Workflow.Entities.Domain;

public partial class ModelParser
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly string _rootPath;

    public Dictionary<string, Role> Roles { get; }
    public Dictionary<string, EntityType> EntityTypes { get; } = new();
    private Dictionary<string, ValueSet> ValueSets { get; }
    private Dictionary<string, Condition> NamedConditions { get; }

    public ModelParser(string rootPath)
    {
        _rootPath = rootPath;

        Roles = Read<Role>();
        ValueSets = Read<ValueSet>();
        NamedConditions = Read<Condition>();

        var entities = Directory.GetDirectories(rootPath)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Where(d => Path.GetFileName(d) != "Common")
            .Select(d => Parse<EntityType>(Path.Combine(d, $"{Path.GetFileName(d)}.yaml")))
            .OrderBy(e => e.InheritsFrom != null);

        foreach (var entity in entities)
        {
            var folder = Path.Combine(_rootPath, entity.Name);
            foreach (var file in Directory.GetFiles(folder, "*.yaml")
                         .Where(f => Path.GetFileNameWithoutExtension(f) != entity.Name))
            {
                var content = Parse<EntityType>(file);
                if (content.Properties.Count > 0) entity.Properties = content.Properties;
                if (content.Actions.Count > 0) entity.Actions = content.Actions;
            }

            entity.Forms = Read<Form>(folder);
            entity.Screens = Read<Screen>(folder);
            entity.AllSteps = Read<Step>(folder);

            foreach (var entry in Read<Condition>(folder))
                NamedConditions.Add(entry.Key, entry.Value);

            if (entity.InheritsFrom != null)
                ApplyInheritance(entity, EntityTypes[entity.InheritsFrom]);

            entity.Steps = entity.StepNames.Select(n => entity.AllSteps[n]).ToList();
            EntityTypes[entity.Name] = entity;

            foreach (var prop in entity.Properties.Where(p => p.Value.UnderlyingType == "User"))
                if (!Roles.ContainsKey(prop.Key))
                    Roles[prop.Key] = new Role { Name = prop.Key };

            foreach (var val in entity.Events)
                val.Value.Name = val.Key;

            foreach (var action in entity.Actions.Except(entity.Parent?.Actions ?? []))
            {
                action.EntityType = entity.Name;
                foreach (var role in action.Roles)
                    Roles[role].Actions.Add(action);
            }

            foreach (var step in
                     entity.AllSteps.Values.Except((IEnumerable<Step>?)entity.Parent?.AllSteps.Values ?? []))
            {
                step.Children = step.ChildNames.Select(s => entity.AllSteps[s]).ToArray();
                foreach (var action in step.Actions)
                {
                    action.EntityType = entity.Name;
                    action.Steps = [step.Name];
                    foreach (var role in action.Roles)
                        Roles[role].Actions.Add(action);
                }

                foreach (var prop in step.Properties)
                    entity.Properties.Add(prop.Key, prop.Value);
            }
        }

        Roles.Values.ForEach(PreProcess);
        ValueSets.Values.ForEach(PreProcess);
        EntityTypes.Values.ForEach(PreProcess);
    }

    private void PreProcess(Role role)
    {
        role.Actions = role.Actions.Union(role.InheritFrom.SelectMany(r => Roles[r].Actions)).ToList();
        foreach (var act in role.Actions)
            PreProcess(act.Triggers);
    }

    private void PreProcess(ValueSet set)
    {
        foreach (var entry in set.Values)
        {
            entry.Value.Name = entry.Key;
            PreProcess(entry.Value);
        }
    }

    private void PreProcess(Form form, EntityType entityType)
    {
        form.EntityType = entityType;

        if (!form.Pages.Any() && form.TargetFormName == null)
            form.Pages["Default"] = new Page
            {
                QuestionNames = entityType.Properties.Values.Select(v => v.Name).ToArray()
            };

        foreach (var ent in form.Pages)
        {
            ent.Value.Name = ent.Key;
            ent.Value.Questions = ent.Value.QuestionNames.Select(q => entityType.Properties[q]).ToArray();
        }

        entityType.Events.Add(form.Name, new() { Name = form.Name });

        if (form is { Property: not null, TargetFormName: not null })
            form.TargetForm = EntityTypes[entityType.Properties[form.Property].UnderlyingType]
                .Forms[form.TargetFormName];

        PreProcess(form.OnSubmit);
        PreProcess(form.OnSave);
    }

    private void PreProcess(Trigger[] triggers)
    {
        foreach (var trigger in triggers)
            PreProcess(trigger.Condition);
    }

    private void PreProcess(EntityType entityType)
    {
        foreach (var ent in entityType.Properties)
        {
            ent.Value.ParentType = entityType;
            ent.Value.Name = ent.Key;
            PreProcess(ent.Value);
        }

        foreach (var form in entityType.Forms.Values)
            PreProcess(form, entityType);
        foreach (var screen in entityType.Screens.Values)
            PreProcess(screen, entityType);
        foreach (var step in entityType.Steps)
            PreProcess(step, entityType);

        entityType.ModelParser = this;
    }

    private void PreProcess(Step step, EntityType entityType)
    {
        PreProcess(step.Condition);
        PreProcess(step.Ends);
        foreach (var ev in step.Actions.SelectMany(a => a.Triggers.Select(t => t.Event)).Where(t => t != null))
            if (!entityType.Events.ContainsKey(ev!))
                entityType.Events.Add(ev!, new() { Name = ev! });
    }

    private void PreProcess(Screen screen, EntityType entityType)
    {
        foreach (var col in screen.Columns)
        {
            if (col.Property != null)
            {
                col.Question = entityType.Properties.GetValueOrDefault(col.Property.Split('.')[0]);
                if (col.Property.EndsWith("Event"))
                    col.Event = entityType.Events.GetValueOrDefault(col.Property[..^5]);
            }
        }
    }

    private Question PreProcess(Question question)
    {
        foreach (var entry in question.Values ?? [])
        {
            entry.Value.Name = entry.Key;
            PreProcess(entry.Value);
        }

        if (ValueSets.TryGetValue(question.UnderlyingType, out var set))
            question.Values = set.Values;
        if (EntityTypes.TryGetValue(question.UnderlyingType, out var type))
            question.EntityType = type;
        PreProcess(question.Condition);
        PreProcess(question.OnSave);
        if (question.Table != null)
            question.Table.Form = question.ParentType.Forms[question.Table.FormReference];

        foreach (var dep in question.Conditions.SelectMany(c => c.Part.Dependants).Distinct())
        {
            var depName = dep.ToString().Split('.').Last();
            question.ParentType.Properties.GetValueOrDefault(depName)?.DependentQuestions.Add(question);
        }

        return question;
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
            return _deserializer.Deserialize<T>(File.ReadAllText(file));
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse {file}: {ex.Message}");
        }
    }


    private Dictionary<string, T> Read<T>(string? root = null)
    {
        root ??= $"{_rootPath}/Common";
        var name = typeof(T).Name;
        var folder = name switch
        {
            _ when name.StartsWith("Variant") => name.Replace("Variant", "") + "s",
            _ => name + "s"
        };
        if (!Directory.Exists($"{root}/{folder}"))
            return new();
        return Directory
            .GetFiles($"{root}/{folder}", "*.yaml")
            .ToDictionary(
                f => Path.GetFileNameWithoutExtension(f),
                f => Parse<T>(f)
            );
    }
}