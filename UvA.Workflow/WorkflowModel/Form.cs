namespace UvA.Workflow.Entities.Domain;

public enum PageLayout
{
    Normal,
    Condensed
}

public enum FormLayout
{
    Normal,
    SinglePage
}

public class Page
{
    public string Name { get; set; } = null!;
    public BilingualString? Title { get; set; }
    public BilingualString? Introduction { get; set; }
    public PageLayout Layout { get; set; }

    [YamlMember(Alias = "questions")]
    [JsonPropertyName("questions")]
    public string[] QuestionNames { get; set; } = [];

    [JsonIgnore] [YamlIgnore] public Question[] Questions { get; set; } = [];

    public string[]? Sources { get; set; }

    public BilingualString DisplayTitle => Title ?? Name;
}

public class Form
{
    public string Name { get; set; } = null!;
    [YamlIgnore] public string? VariantName { get; set; }
    public BilingualString? Title { get; set; }
    public BilingualString DisplayName => Title ?? Name;
    public FormLayout Layout { get; set; }

    public string? Property { get; set; }

    [YamlMember(Alias = "targetForm")]
    [JsonPropertyName("targetForm")]
    public string? TargetFormName { get; set; }

    [JsonIgnore] [YamlIgnore] public Form? TargetForm { get; set; }

    [YamlIgnore] public Form ActualForm => TargetForm ?? this;

    public Dictionary<string, Page> Pages { get; set; } = new();

    [YamlIgnore] public EntityType EntityType { get; set; } = null!;

    public Trigger[] OnSubmit { get; set; } = [];
    public Trigger[] OnSave { get; set; } = [];
    public IEnumerable<Question> Questions => Pages.Values.SelectMany(p => p.Questions);
}