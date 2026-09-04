namespace UvA.Workflow.Migrations;

/// <summary>A migration declared in a workflow YAML configuration.</summary>
public class ConfiguredMigration : INamed
{
    public const string RenamePropertyKind = "renameProperty";

    [YamlIgnore] public string Name { get; set; } = null!;
    [YamlIgnore] public string WorkflowDefinition { get; set; } = null!;
    [YamlIgnore] public string MigrationId => $"{WorkflowDefinition}:{Name}";

    /// <summary>Currently only <c>renameProperty</c> is supported.</summary>
    public MigrationKind Kind { get; set; }

    public string OldProperty { get; set; } = null!;
    public string NewProperty { get; set; } = null!;
}