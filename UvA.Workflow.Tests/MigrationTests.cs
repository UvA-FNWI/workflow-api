using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Migrations;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class MigrationTests
{
    [Fact]
    public void Validator_AcceptsCompatibleRename()
    {
        var active = CreateParser("Title", "String!");
        var target = CreateParser("ProjectTitle", "String");

        MigrationValidator.ValidatePropertyRename(active, target, "Project", "Title", "ProjectTitle");
    }

    [Fact]
    public void Validator_RejectsRenameThatChangesStoredType()
    {
        var active = CreateParser("Title", "String");
        var target = CreateParser("ProjectTitle", "Int");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MigrationValidator.ValidatePropertyRename(active, target, "Project", "Title", "ProjectTitle"));

        Assert.Contains("changes the stored type", exception.Message);
    }

    [Fact]
    public void PendingBaseline_DoesNotReplaceActiveConfiguration()
    {
        var context = new DefaultHttpContext();
        var resolver = new ModelServiceResolver(new HttpContextAccessor { HttpContext = context });
        var active = CreateParser("Title", "String");
        var pending = CreateParser("ProjectTitle", "String");
        resolver.AddOrUpdate("", active, "active-layout", "source-sha", VersionKind.Baseline);

        resolver.StagePendingBaseline(pending, "pending-layout", "target-sha");

        Assert.Contains(resolver.Resolve().ModelService.WorkflowDefinitions["Project"].Properties,
            property => property.Name == "Title");
        Assert.Equal("active-layout", resolver.Resolve().DefaultMailLayout);
        Assert.Equal("target-sha", resolver.GetPendingBaseline()?.TargetCommit);
    }

    [Fact]
    public async Task Compatibility_OldDocumentCanBeReadAndWrittenThroughEitherName()
    {
        var store = new Mock<IMigrationStore>();
        store.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ActiveRename()]);
        var instance = Instance(new Dictionary<string, BsonValue> { ["Title"] = "Original" });

        await new MigrationCompatibilityService(store.Object).Attach(instance);

        Assert.Equal("Original", instance.GetProperty("Title")?.AsString);
        Assert.Equal("Original", instance.GetProperty("ProjectTitle")?.AsString);
        Assert.Equal("Original", instance.Properties["ProjectTitle"].AsString);

        instance.SetProperty("Changed by old code", "Title");

        Assert.Equal("Changed by old code", instance.Properties["Title"].AsString);
        Assert.Equal("Changed by old code", instance.Properties["ProjectTitle"].AsString);
    }

    [Fact]
    public async Task Compatibility_StopsAfterOldFieldRemovalBegins()
    {
        var migration = ActiveRename();
        migration.Stage = MigrationStage.RemovingOldName;
        var store = new Mock<IMigrationStore>();
        store.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([migration]);
        var aliases = await new MigrationCompatibilityService(store.Object).GetAliases("Project");

        Assert.Empty(aliases);
    }

    [Fact]
    public void Compatibility_SupportsNestedPropertyPaths()
    {
        var instance = Instance(new Dictionary<string, BsonValue>
        {
            ["Details"] = new BsonDocument("Title", "Original")
        });
        instance.PropertyRenameAliases =
        [
            new PropertyRenameAlias("Project", "Details.Title", "Details.ProjectTitle")
        ];

        instance.MaterializeMissingPropertyAliases();
        instance.SetProperty("Changed", "Details", "ProjectTitle");

        var details = instance.Properties["Details"].AsBsonDocument;
        Assert.Equal("Changed", details["Title"].AsString);
        Assert.Equal("Changed", details["ProjectTitle"].AsString);
    }

    private static Migration ActiveRename() => new()
    {
        Id = "Project:RenameTitleToProjectTitle",
        Kind = MigrationKind.RenameProperty,
        WorkflowDefinition = "Project",
        OldPath = "Title",
        NewPath = "ProjectTitle",
        SourceCommit = "source",
        TargetCommit = "target",
        Stage = MigrationStage.SupportingBothNames,
        RunStatus = MigrationRunStatus.Running,
        RequestedBy = "admin",
        RequestedAt = DateTime.UtcNow
    };

    private static WorkflowInstance Instance(Dictionary<string, BsonValue> properties) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        WorkflowDefinition = "Project",
        Properties = properties,
        Events = []
    };

    private static ModelParser CreateParser(string propertyName, string type)
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
        return new ModelParser(new DictionaryProvider(files));
    }
}