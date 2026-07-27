namespace UvA.Workflow.WorkflowModel;

/// <summary>Top-level keys present in the source YAML.</summary>
public interface IDeclaredKeys
{
    [YamlIgnore] HashSet<string> DeclaredKeys { get; set; }
}

public static class DeclaredKeysExtensions
{
    public static bool Declared(this IDeclaredKeys source, string yamlKey) => source.DeclaredKeys.Contains(yamlKey);
}