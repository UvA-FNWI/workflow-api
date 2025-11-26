using UvA.Workflow.Expressions;

namespace UvA.Workflow.Entities.Domain;

/// <summary>
/// Represents a type of object ("entity") in the workflow system
/// </summary>
public class WorkflowDefinition
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

    private Template? _instanceTitleTemplate;
    public Template? InstanceTitleTemplate => _instanceTitleTemplate ??= Template.Create(InstanceTitle);

    /// <summary>
    /// Dictionary of properties for this entity type
    /// </summary>
    public Dictionary<string, PropertyDefinition> Properties { get; set; } = new();

    /// <summary>
    /// Dictionary of event definitions for this entity type
    /// </summary>
    public Dictionary<string, EventDefinition> Events { get; set; } = new();

    /// <summary>
    /// List of actions for this entity type
    /// </summary>
    public List<Action> GlobalActions { get; set; } = [];

    /// <summary>
    /// List of step names for this entity type
    /// </summary>
    [YamlMember(Alias = "steps")]
    public List<string> StepNames { get; set; } = [];

    /// <summary>
    /// List of header fields for this entity type
    /// </summary>
    public Field[] HeaderFields { get; set; } = [];

    /// <summary>
    /// List of computed results for this entity type
    /// </summary>
    public Result[]? Results { get; set; }

    /// <summary>
    /// Indicated whether this entity type is stored as an embedded document in the parent instance
    /// </summary>
    public bool IsEmbedded { get; set; }

    [YamlIgnore] public ModelParser ModelParser { get; set; } = null!;

    [YamlIgnore] public Dictionary<string, Form> Forms { get; set; } = null!;
    [YamlIgnore] public Dictionary<string, Step> AllSteps { get; set; } = null!;
    [YamlIgnore] public Dictionary<string, Screen> Screens { get; set; } = null!;
    [YamlIgnore] public List<Step> Steps { get; set; } = [];
    [YamlIgnore] public WorkflowDefinition? Parent { get; set; }

    public DataType GetDataType(string property)
    {
        if (Properties.TryGetValue(property, out var prop))
            return prop.DataType;
        if (property.EndsWith("Event") && Events.ContainsKey(property[..^5]))
            return DataType.DateTime;
        return DataType.String;
    }

    public string GetKey(string property)
    {
        if (Properties.ContainsKey(property))
            return $"$Properties.{property}";
        if (property.EndsWith("Event") && Events.ContainsKey(property[..^5]))
            return $"$Events.{property[..^5]}.Date";
        return "$" + property;
    }
}

public class EventDefinition
{
    /// <summary>
    /// Name of the event
    /// </summary>
    public string Name { get; set; } = null!;
}