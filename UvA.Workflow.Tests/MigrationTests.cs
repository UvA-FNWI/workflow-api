using Microsoft.AspNetCore.Http;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Migrations;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class MigrationTests
{
    [Fact]
    public void ModelParser_LoadsMigrationFromWorkflowMigrationsFolder()
    {
        var parser = CreateParser("ProjectTitle", "String", MigrationYaml());

        var migration = Assert.Single(parser.Migrations);
        Assert.Equal("Project:RenameTitleToProjectTitle", migration.Id);
        Assert.Equal(MigrationKind.RenameProperty, migration.MigrationKind);
        Assert.Equal("Title", migration.OldPath);
        Assert.Equal("ProjectTitle", migration.NewPath);
        Assert.Equal(64, migration.Checksum.Length);
        Assert.Same(migration, Assert.Single(parser.WorkflowDefinitions["Project"].Migrations));
    }

    [Fact]
    public void Compare_RecognizesCompatibleRenameInCombinedConfigurationChange()
    {
        var active = CreateParser("Title", "String!");
        var pending = CreateParser("ProjectTitle", "String", MigrationYaml());

        var introduced = MigrationPlanValidator.Compare(active, pending);

        Assert.Equal("Project:RenameTitleToProjectTitle", Assert.Single(introduced).Id);
    }

    [Fact]
    public void Compare_RejectsRenameThatAlsoChangesStoredType()
    {
        var active = CreateParser("Title", "String");
        var pending = CreateParser("ProjectTitle", "Int", MigrationYaml());

        var exception = Assert.Throws<Exception>(() =>
            MigrationPlanValidator.Compare(active, pending));

        Assert.Contains("changes the stored type", exception.Message);
    }

    [Fact]
    public void Compare_RejectsEditingAnExistingMigrationPlan()
    {
        var active = CreateParser("ProjectTitle", "String", MigrationYaml());
        var edited = CreateParser("RenamedAgain", "String", """
                                                            kind: renameProperty
                                                            oldPath: Title
                                                            newPath: RenamedAgain
                                                            """);

        var exception = Assert.Throws<Exception>(() =>
            MigrationPlanValidator.Compare(active, edited));

        Assert.Contains("cannot be edited", exception.Message);
    }

    [Fact]
    public void PendingBaseline_DoesNotReplaceActiveConfiguration()
    {
        var context = new DefaultHttpContext();
        var resolver = new ModelServiceResolver(new HttpContextAccessor { HttpContext = context });
        var active = CreateParser("Title", "String");
        var pending = CreateParser("ProjectTitle", "String", MigrationYaml());
        resolver.AddOrUpdate("", active, "active-layout", "source-sha", VersionKind.Baseline);

        resolver.StagePendingBaseline(pending, "pending-layout", "target-sha");

        Assert.Contains(resolver.Resolve().ModelService.WorkflowDefinitions["Project"].Properties,
            property => property.Name == "Title");
        Assert.Equal("active-layout", resolver.Resolve().DefaultMailLayout);
        Assert.Equal("target-sha", resolver.GetPendingBaseline()?.TargetCommit);
        Assert.Equal("ProjectTitle", Assert.Single(resolver.GetPendingMigrationPlans()).NewPath);
    }

    private static ModelParser CreateParser(string propertyName, string type, string? migration = null)
    {
        var files = new Dictionary<string, string>
        {
            ["Projects/Project/Entity.yaml"] = $$"""
                                                 name: Project
                                                 titlePlural: Projects
                                                 properties:
                                                   - name: {{propertyName}}
                                                     type: {{type}}
                                                 """
        };
        if (migration != null)
            files["Projects/Project/Migrations/RenameTitleToProjectTitle.yaml"] = migration;
        return new ModelParser(new DictionaryProvider(files));
    }

    private static string MigrationYaml() => """
                                             kind: renameProperty
                                             oldPath: Title
                                             newPath: ProjectTitle
                                             description: Use the more specific project title name.
                                             """;
}