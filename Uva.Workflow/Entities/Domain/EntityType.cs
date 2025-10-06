using System.Text.Json.Serialization;
using Uva.Workflow.Expressions;
using YamlDotNet.Serialization;

namespace Uva.Workflow.Entities.Domain;

public class EntityType
{
    public string Name { get; set; } = null!;
    public BilingualString? Title { get; set; }
    public BilingualString TitlePlural { get; set; } = null!;
    public BilingualString DisplayTitle => Title ?? Name;
    public int? Index { get; set; }
    public bool IsAlwaysVisible { get; set; }
    public string? InheritsFrom { get; set; }
    
    public string? InstanceTitle { get; set; }
    private Template? _instanceTitleTemplate;
    public Template? InstanceTitleTemplate => _instanceTitleTemplate ??= Template.Create(InstanceTitle);
    
    public Dictionary<string, Question> Properties { get; set; } = new();
    public Dictionary<string, EventDefinition> Events { get; set; } = new();
    
    public List<Action> Actions { get; set; } = [];
    [YamlMember(Alias = "steps")]
    [JsonPropertyName("steps")]
    public List<string> StepNames { get; set; } = [];
    public SeedData? SeedData { get; set; }
    public Field[] HeaderFields { get; set; } = [];
    public Result[]? Results { get; set; }
    public bool IsEmbedded { get; set; }
    
    [YamlIgnore] public ModelParser ModelParser { get; set; } = null!;
    
    [JsonIgnore] [YamlIgnore] public Dictionary<string, Form> Forms { get; set; } = null!;
    [JsonIgnore] [YamlIgnore] public Dictionary<string, Step> AllSteps { get; set; } = null!;
    [JsonIgnore] [YamlIgnore] public Dictionary<string, Screen> Screens { get; set; } = null!;
    [JsonIgnore] [YamlIgnore] public List<Step> Steps { get; set; } = [];
    [JsonIgnore] [YamlIgnore] public EntityType? Parent { get; set; }

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
    public string Name { get; set; } = null!;
}

public class SeedData
{
    public string MatchBy { get; set; } = null!;
    public Dictionary<string, string>[] Data { get; set; } = null!;
}