namespace UvA.Workflow.Migrations;

/// <summary>A migration declared in a workflow YAML configuration.</summary>
public class ConfiguredMigration : INamed
{
    public const string CommonScope = "Common";

    [YamlIgnore] public string Name { get; set; } = null!;

    /// <summary>The workflow names targeted by this migration.</summary>
    [YamlIgnore]
    public string[] WorkflowDefinitions { get; set; } = [];

    /// <summary>The declaring workflow name or <see cref="CommonScope"/>, used for a stable migration identity.</summary>
    [YamlIgnore]
    public string Scope { get; set; } = null!;

    [YamlIgnore] public string MigrationId => $"{Scope}:{Name}";

    /// <summary>Currently only <c>RenameProperty</c> is supported.</summary>
    public MigrationKind Kind { get; set; }

    public string OldProperty { get; set; } = null!;
    public string NewProperty { get; set; } = null!;
}