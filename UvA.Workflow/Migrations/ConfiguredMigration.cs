namespace UvA.Workflow.Migrations;

/// <summary>A migration declared in a workflow YAML configuration.</summary>
public class ConfiguredMigration : INamed
{
    [YamlIgnore] public string Name { get; set; } = null!;

    /// <summary>The name of the workflow declaring this migration.
    /// Determines the workflow targets and forms part of the stable migration identity.</summary>
    [YamlIgnore]
    public string Scope { get; set; } = null!;

    [YamlIgnore] public string MigrationId => $"{Scope}:{Name}";

    /// <summary>Currently only <c>RenameProperty</c> is supported.</summary>
    public MigrationKind Kind { get; set; }

    public string OldProperty { get; set; } = null!;
    public string NewProperty { get; set; } = null!;
}