using UvA.Workflow.WorkflowModel;

public class StepTemplate : Step
{
    [YamlMember(Alias = "params")] public List<StepTemplateParameter> Parameters { get; set; } = [];
    [YamlIgnore] public string RootFile { get; set; } = null!;
}

public class StepTemplateParameter : INamed
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
        "DateTime" => DataType.DateTime,
        "Date" => DataType.Date,
        "Int" => DataType.Int,
        "Double" => DataType.Double,
        "File" => DataType.File,
        "User" => DataType.User,
        "Currency" => DataType.Currency,
        "Boolean" => DataType.Boolean,
        _ => throw new ArgumentException($"Invalid type {UnderlyingType}")
    };
}

public class TemplateValue : INamed
{
    public string Name { get; set; } = null!;
    public object Value { get; set; } = null!;
}