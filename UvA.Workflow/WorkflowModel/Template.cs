using UvA.Workflow.WorkflowModel;

public interface ITemplate : INamed
{
    List<TemplateParameter> Parameters { get; }
    string RootFile { get; set; }
}

public interface ITemplateConsumer : INamed, IDeclaredKeys
{
    string? TemplateKey { get; }
    Dictionary<string, object>? TemplateValues { get; }
    Dictionary<string, object>? ResolvedTemplateValues { get; set; }
}

public class StepTemplate : Step, ITemplate
{
    [YamlMember(Alias = "params")] public List<TemplateParameter> Parameters { get; set; } = [];
    [YamlIgnore] public string RootFile { get; set; } = null!;
}

public class FormTemplate : Form, ITemplate
{
    [YamlMember(Alias = "params")] public List<TemplateParameter> Parameters { get; set; } = [];
    [YamlIgnore] public string RootFile { get; set; } = null!;
}

public class TemplateParameter : INamed
{
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;

    public object? Default { get; set; }
    [YamlIgnore] public string UnderlyingType => Type.TrimEnd('!', ']').TrimStart('[');

    [YamlIgnore] public bool IsRequired => Type.EndsWith('!');
    [YamlIgnore] public bool IsArray => Type.StartsWith('[');

    [YamlIgnore]
    public DataType DataType => UnderlyingType switch
    {
        "String" => DataType.String,
        "LocalString" => DataType.LocalString,
        "DateTime" => DataType.DateTime,
        "Date" => DataType.Date,
        "Int" => DataType.Int,
        "Double" => DataType.Double,
        "File" => DataType.File,
        "User" => DataType.User,
        "Currency" => DataType.Currency,
        "Check" => DataType.Check,
        _ => throw new ArgumentException($"Invalid type {UnderlyingType}")
    };
}