namespace Uva.Workflow.Entities.Domain;

public enum QuestionKind
{
    Normal,
    Hidden
}

public enum QuestionLayout
{
    Standard,
    RadioList
}

public class Question
{
    public string Name { get; set; } = null!;
    public BilingualString? Text { get; set; }
    public BilingualString DisplayName => Text ?? Name;
    public BilingualString ShortDisplayName => ShortText ?? Text ?? Name;
    public QuestionKind Kind { get; set; }
    public QuestionLayout Layout { get; set; }
    public BilingualString? ShortText { get; set; }
    public string Type { get; set; } = null!;
    public Dictionary<string, Choice>? Values { get; set; }
    public BilingualString? Description { get; set; }
    public bool Multiline { get; set; }
    public string? Default { get; set; }
    public string? ReplacedBy { get; set; }

    [YamlIgnore] [JsonIgnore] public EntityType ParentType { get; set; } = null!;

    [YamlIgnore] [JsonIgnore] public EntityType? EntityType { get; set; }

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

    public Condition? Condition { get; set; }
    public Condition? Validation { get; set; }

    [JsonIgnore]
    [YamlIgnore]
    public IEnumerable<Condition> Conditions =>
        (Values?.Values.Select(v => v.Condition) ?? []).Append(Condition).Append(Validation).Where(c => c != null)!;

    public List<Question> DependentQuestions { get; } = [];

    public Trigger[] OnSave { get; set; } = [];
    public bool IsContext { get; set; }
    public bool AllowAttachments { get; set; }
    public bool HideInResults { get; set; }

    public TableSettings? Table { get; set; }
}

public class TableSettings
{
    [JsonPropertyName("form")]
    [YamlMember(Alias = "form")]
    public string FormReference { get; set; } = null!;

    [YamlIgnore] [JsonIgnore] public Form Form { get; set; } = null!;
    public TableLayout Layout { get; set; }
}

public enum TableLayout
{
    InlineEditing,
    Modal
}

public class Choice
{
    public string Name { get; set; } = null!;
    public BilingualString? Text { get; set; }
    public BilingualString? Description { get; set; }
    public double? Value { get; set; }

    public Condition? Condition { get; set; }

    public static implicit operator Choice(string value) => new Choice { Text = value };
}