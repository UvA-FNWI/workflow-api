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

/// <summary>
/// Represents a page in a form
/// </summary>
public class Page
{
    /// <summary>
    /// Internal name of the page
    /// </summary>
    [YamlIgnore]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized title of the page
    /// </summary>
    public BilingualString? Title { get; set; }

    /// <summary>
    /// Localized introduction text to show at the start of the page
    /// </summary>
    public BilingualString? Introduction { get; set; }

    /// <summary>
    /// Layout of the page. Condensed will show the questions in a table
    /// </summary>
    public PageLayout Layout { get; set; }

    /// <summary>
    /// Question names to include in the page
    /// </summary>
    [YamlMember(Alias = "questions")]
    public string[] QuestionNames { get; set; } = [];

    [YamlIgnore] public Question[] Questions { get; set; } = [];

    /// <summary>
    /// If set, this page is included only when editing a matching property
    /// </summary>
    public string[]? Sources { get; set; }

    public BilingualString DisplayTitle => Title ?? Name;
}

public class Form
{
    /// <summary>
    /// Internal name of the form
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Bilingual title of the form
    /// </summary>
    public BilingualString? Title { get; set; }

    public BilingualString DisplayName => Title ?? Name;

    /// <summary>
    /// Sets whether this form shows as multi-page layout with navigation bor or shows all pages at once
    /// </summary>
    public FormLayout Layout { get; set; }

    /// <summary>
    /// Target reference property. Set this to use the form to update the properties of the referenced entity
    /// </summary>
    public string? Property { get; set; }

    /// <summary>
    /// To be used in combination with Property. The name of a form for the referenced entity type
    /// </summary>
    [YamlMember(Alias = "targetForm")]
    public string? TargetFormName { get; set; }

    [YamlIgnore] public Form? TargetForm { get; set; }

    public Form ActualForm => TargetForm ?? this;

    /// <summary>
    /// Dictionary of pages of this form
    /// </summary>
    public Dictionary<string, Page> Pages { get; set; } = new();

    [YamlIgnore] public EntityType EntityType { get; set; } = null!;

    /// <summary>
    /// Triggers to run when the form is submitted
    /// </summary>
    public Trigger[] OnSubmit { get; set; } = [];

    /// <summary>
    /// Triggers to run when a change is made in the form
    /// </summary>
    public Trigger[] OnSave { get; set; } = [];

    public IEnumerable<Question> Questions => Pages.Values.SelectMany(p => p.Questions);
}