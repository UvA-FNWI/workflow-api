using UvA.Workflow.Expressions;
using Action = UvA.Workflow.Entities.Domain.Action;

namespace UvA.Workflow.WorkflowModel;

/// <summary>
/// Represents a type of object ("entity") in the workflow system
/// </summary>
public class WorkflowDefinition : INamed
{
    /// <summary>
    /// Short internal name of the entity type
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized title of the entity type
    /// </summary>
    public BilingualString? Title { get; set; }

    /// <summary>
    /// Localized plural of the entity type title
    /// </summary>
    public BilingualString TitlePlural { get; set; } = null!;

    public BilingualString DisplayTitle => Title ?? Name;

    /// <summary>
    /// Integer indicating the tab order in the instance page
    /// </summary>
    public int? Index { get; set; }

    /// <summary>
    /// Always show this entity type to the user, even if the user has no rights 
    /// </summary>
    public bool IsAlwaysVisible { get; set; }

    /// <summary>
    /// Name of the entity type this type inherits from
    /// </summary>
    public string? InheritsFrom { get; set; }

    /// <summary>
    /// Template for the title of an instance
    /// </summary>
    public string? InstanceTitle { get; set; }

    public Template? InstanceTitleTemplate => field ??= Template.Create(InstanceTitle);

    /// <summary>
    /// Dictionary of properties for this entity type
    /// </summary>
    public List<PropertyDefinition> Properties { get; set; } = new();

    /// <summary>
    /// Dictionary of event definitions for this entity type
    /// </summary>
    public List<EventDefinition> Events { get; set; } = new();

    /// <summary>
    /// List of actions for this entity type
    /// </summary>
    public List<Action> GlobalActions { get; set; } = [];

    public IEnumerable<Action> AllActions => GlobalActions.Concat(AllSteps.SelectMany(s => s.Actions));

    /// <summary>
    /// List of step names for this entity type
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<string> StepNames { get; set; } = [];

    /// <summary>
    /// List of fields for this entity type
    /// </summary>
    public Field[] Fields { get; set; } = [];

    /// <summary>
    /// List of computed results for this entity type
    /// </summary>
    public Result[]? Results { get; set; }

    /// <summary>
    /// Indicated whether this entity type is stored as an embedded document in the parent instance
    /// </summary>
    public bool IsEmbedded { get; set; }

    /// <summary>
    /// Data to be automatically created on app start. Will match existing data by external ID
    /// </summary>
    public Dictionary<string, string>[]? SeedData { get; set; }

    [YamlIgnore] public ModelParser ModelParser { get; set; } = null!;

    [YamlIgnore] public List<Form> Forms { get; set; } = null!;
    [YamlIgnore] public List<Step> AllSteps { get; set; } = null!;
    [YamlIgnore] public List<Screen> Screens { get; set; } = null!;
    [YamlIgnore] public List<Step> Steps { get; set; } = [];
    [YamlIgnore] public WorkflowDefinition? Parent { get; set; }

    private static IEnumerable<Step> GetSteps(Step s) =>
        s.Children.Any() && s.HierarchyMode == StepHierarchyMode.Sequential
            ? s.Children.SelectMany(GetSteps)
            : [s];

    public IEnumerable<Step> FlattenedSteps => Steps.SelectMany(GetSteps);

    public DataType GetDataType(string property)
    {
        if (Properties.TryGetValue(property, out var prop))
            return prop.DataType;
        if (property.EndsWith("Event") && Events.Contains(property[..^5]))
            return DataType.DateTime;
        return DataType.String;
    }

    public string GetKey(string property)
    {
        if (Properties.Contains(property))
            return $"$Properties.{property}";
        if (property.EndsWith("Event") && Events.Contains(property[..^5]))
            return $"$Events.{property[..^5]}.Date";
        return "$" + property;
    }
}

public class EventDefinition : INamed
{
    /// <summary>
    /// Name of the event
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// List of event names that are suppressed when this event occurs. Only the most recent event will be suppressed.
    /// </summary>
    public List<string>? Suppresses { get; set; }
}