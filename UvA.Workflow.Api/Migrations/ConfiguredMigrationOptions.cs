namespace UvA.Workflow.Api.Migrations;

public class ConfiguredMigrationOptions
{
    public const string SectionName = "ConfiguredMigrations";

    public bool Enabled { get; set; } = true;
}