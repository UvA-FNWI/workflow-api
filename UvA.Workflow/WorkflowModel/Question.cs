namespace UvA.Workflow.Entities.Domain;

public enum QuestionKind
{
    Normal,
    Hidden
}

public enum ChoiceLayoutType
{
    Dropdown,
    RadioList
}

public class LayoutOptions;

public class ChoiceLayoutOptions : LayoutOptions
{
    /// <summary>
    /// Set if the question should be shown as dropdown or radio list
    /// </summary>
    public ChoiceLayoutType Type { get; set; }
}

public class StringLayoutOptions : LayoutOptions
{
    /// <summary>
    /// Set if the question should be a multiline text field
    /// </summary>
    public bool Multiline { get; set; }

    /// <summary>
    /// Set if the text field should allow attachments 
    /// </summary>
    public bool AllowAttachments { get; set; }
}

/// <summary>
/// Represents a property of an entity type (which can also be used as a question in a form)
/// </summary>
public class Question
{
    /// <summary>
    /// Internal name of the question
    /// </summary>
    [YamlIgnore]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized text of the question
    /// </summary>
    public BilingualString? Text { get; set; }

    public BilingualString DisplayName => Text ?? Name;
    public BilingualString ShortDisplayName => ShortText ?? Text ?? Name;

    /// <summary>
    /// Set if this question is hidden from users without extra permissions
    /// </summary>
    public QuestionKind Kind { get; set; }

    /// <summary>
    /// Specific layout options for the data type
    /// </summary>
    /// <remarks>
    /// This is strongly typed in the yaml schema and in the frontend, but not in the backend since we don't use it
    /// </remarks>
    public Dictionary<string, object>? Layout { get; set; }

    /// <summary>
    /// Localized short question text to shown in results  
    /// </summary>
    public BilingualString? ShortText { get; set; }

    /// <summary>
    /// Data type of the question. Can be a primitive type String, Int, Double, DateTime, Date, User, Currency, File,
    /// or a reference to a value set or another entity type. Use [Type] to indicate an array and Type! to indicate
    /// a required value.
    /// </summary>
    public string Type { get; set; } = null!;

    /// <summary>
    /// Values for a choice question.
    /// </summary>
    public Dictionary<string, Choice>? Values { get; set; }

    /// <summary>
    /// Localized extended description text for the question.
    /// </summary>
    public BilingualString? Description { get; set; }

    [YamlIgnore] public EntityType ParentType { get; set; } = null!;

    [YamlIgnore] public EntityType? EntityType { get; set; }

    public string UnderlyingType => Type.TrimEnd('!', ']').TrimStart('[');

    public bool IsRequired => Type.EndsWith('!');
    public bool IsArray => Type.StartsWith('[');

    public DataType DataType => UnderlyingType switch
    {
        "String" => DataType.String,
        "DateTime" => DataType.DateTime,
        "Date" => DataType.Date,
        "Int" => DataType.Int,
        "Double" => DataType.Double,
        "File" => DataType.File,
        "User" => DataType.User,
        "Currency" => DataType.Currency,
        "Table" => DataType.Table,
        _ when EntityType != null => DataType.Reference,
        _ when Values != null => DataType.Choice,
        _ => throw new ArgumentException("Invalid type")
    };

    /// <summary>
    /// Condition that determines if the question should be shown
    /// </summary>
    public Condition? Condition { get; set; }

    /// <summary>
    /// Condition that determines if the value is valid 
    /// </summary>
    public Condition? Validation { get; set; }

    [YamlIgnore]
    public IEnumerable<Condition> Conditions =>
        (Values?.Values.Select(v => v.Condition) ?? []).Append(Condition).Append(Validation).Where(c => c != null)!;

    public List<Question> DependentQuestions { get; } = [];

    /// <summary>
    /// Trigger that is run whenever a value is changed for this property
    /// </summary>
    public Trigger[] OnSave { get; set; } = [];

    /// <summary>
    /// Determines if the question should be hidden in the results table
    /// </summary>
    public bool HideInResults { get; set; }

    /// <summary>
    /// Configure table for the Table data type
    /// </summary>
    public TableSettings? Table { get; set; }
}

public class TableSettings
{
    [YamlMember(Alias = "form")] public string FormReference { get; set; } = null!;

    [YamlIgnore] public Form Form { get; set; } = null!;
    public TableLayout Layout { get; set; }
}

public enum TableLayout
{
    InlineEditing,
    Modal
}

public class Choice
{
    /// <summary>
    /// Internal name of the choice
    /// </summary>
    [YamlIgnore]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Localized text of the choice
    /// </summary>
    public BilingualString? Text { get; set; }

    /// <summary>
    /// Localized extended description text for the choice
    /// </summary>
    public BilingualString? Description { get; set; }

    /// <summary>
    /// Numeric value of the choice
    /// </summary>
    public double? Value { get; set; }

    /// <summary>
    /// Condition that determines if the choice should be shown
    /// </summary>
    public Condition? Condition { get; set; }

    public static implicit operator Choice(string value) => new Choice { Text = value };
}