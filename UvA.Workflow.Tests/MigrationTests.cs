using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Migrations;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class MigrationTests
{
    [Fact]
    public void Migration_RoundTripsThroughMongoSerialization()
    {
        var migration = ReadyMigration();
        var document = migration.ToBsonDocument();

        var restored = BsonSerializer.Deserialize<Migration>(migration.ToBson());

        Assert.Equal(BsonType.ObjectId, document["_id"].BsonType);
        Assert.Equal("migration-id", document["MigrationId"].AsString);
        Assert.Equal("Project", document["Scope"].AsString);
        Assert.Equal("Project", restored.Scope);
        Assert.Equal(BsonType.Array, document["WorkflowDefinitions"].BsonType);
        Assert.Equal("Title", document["OldProperty"].AsString);
        Assert.Equal(["Project"], restored.WorkflowDefinitions);
        Assert.Equal("Title", restored.OldProperty);
        Assert.Equal("ProjectTitle", restored.NewProperty);
    }

    [Fact]
    public async Task RunConfigured_RunsToCompletionImmediately()
    {
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.RenamePropertyValues(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyRenameResult(3, 3));
        repository.Setup(value => value.RenameJournalPaths(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var service = CreateService(repository);

        var migration = await service.RunConfigured(CreateConfiguredMigration("Project"));

        Assert.Equal(MigrationStatus.Finished, migration.Status);
        Assert.Equal(3, migration.ItemsMatched);
        Assert.Equal(3, migration.ItemsUpdated);
        Assert.Equal(["Project"], migration.WorkflowDefinitions);
        Assert.Equal("Title", migration.OldProperty);
        Assert.Equal("ProjectTitle", migration.NewProperty);
        Assert.Equal(2, migration.JournalEntriesUpdated);
        Assert.NotNull(migration.FinishedAt);
        repository.Verify(value => value.Create(migration, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.Update(migration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunConfigured_AllowsModelContainingOnlyNewProperty()
    {
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.RenamePropertyValues(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyRenameResult(3, 3));
        var service = CreateService(repository, CreateParser("ProjectTitle"));

        var migration = await service.RunConfigured(CreateConfiguredMigration("Project"));

        Assert.Equal(MigrationStatus.Finished, migration.Status);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Common")]
    public async Task RunConfigured_RequiresAWorkflowScope(string scope)
    {
        var repository = new Mock<IMigrationRepository>();
        var service = CreateService(repository);

        var error = await Assert.ThrowsAsync<MigrationValidationException>(() =>
            service.RunConfigured(CreateConfiguredMigration(scope)));

        Assert.Equal("MigrationUnknownWorkflow", error.Code);
        Assert.Equal($"Unknown workflow '{scope}'", error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("Title", "ProjectTitle")]
    [InlineData("Code")]
    public async Task RunConfigured_RejectsModelUnlessOnlyNewPropertyIsPresent(params string[] properties)
    {
        var repository = new Mock<IMigrationRepository>();
        var parser = CreateParser(properties);
        var service = CreateService(repository, parser);

        var error = await Assert.ThrowsAsync<MigrationValidationException>(() =>
            service.RunConfigured(CreateConfiguredMigration("Project")));

        Assert.Equal("MigrationInvalidModelState", error.Code);
        Assert.Equal("Workflow 'Project' must contain 'ProjectTitle' and must not contain 'Title'", error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Project", "Code", "ProjectCode")]
    [InlineData("Course", "Title", "ProjectTitle")]
    public async Task RunConfigured_AllowsMigrationWithoutWorkflowAndPropertyOverlap(
        string existingWorkflow,
        string existingOldProperty,
        string existingNewProperty)
    {
        var repository = new Mock<IMigrationRepository>();
        var existing = ReadyMigration();
        existing.WorkflowDefinitions = [existingWorkflow];
        existing.OldProperty = existingOldProperty;
        existing.NewProperty = existingNewProperty;
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);
        repository.Setup(value => value.RenamePropertyValues(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyRenameResult(3, 3));
        var service = CreateService(repository);

        var migration = await service.RunConfigured(CreateConfiguredMigration("Project"));

        Assert.Equal(MigrationStatus.Finished, migration.Status);
        repository.Verify(value => value.Create(migration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Title", "OtherTitle", "Project.Title")]
    [InlineData("LegacyTitle", "ProjectTitle", "Project.ProjectTitle")]
    public async Task RunConfigured_RejectsOverlappingMigrationForTheSameWorkflow(
        string existingOldProperty,
        string existingNewProperty,
        string expectedConflict)
    {
        var repository = new Mock<IMigrationRepository>();
        var existing = ReadyMigration();
        existing.OldProperty = existingOldProperty;
        existing.NewProperty = existingNewProperty;
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);
        var service = CreateService(repository);

        var error = await Assert.ThrowsAsync<MigrationValidationException>(() =>
            service.RunConfigured(CreateConfiguredMigration("Project")));

        Assert.Equal("MigrationPropertyOverlap", error.Code);
        Assert.Contains(expectedConflict, error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void ModelParser_ReadsMigrationsFromWorkflowFolder()
    {
        var parser = CreateConfiguredParser();

        var migration = Assert.Single(parser.Migrations);
        Assert.Equal("Project:rename-title", migration.MigrationId);
        Assert.Equal("Project", migration.Scope);
        Assert.Equal(MigrationKind.RenameProperty, migration.Kind);
        Assert.Equal("Title", migration.OldProperty);
        Assert.Equal("ProjectTitle", migration.NewProperty);
    }

    [Theory]
    [InlineData("Project-Base", new[] { "Project-Base", "Project-Child", "Project-Leaf", "Project-Sibling" })]
    [InlineData("Project-Child", new[] { "Project-Child", "Project-Leaf" })]
    [InlineData("Project-Leaf", new[] { "Project-Leaf" })]
    public async Task RunConfigured_WorkflowScopeTargetsOnlyItselfAndDescendants(string scope, string[] expectedTargets)
    {
        var parser = CreateInheritedMigrationParser();

        Assert.Equal(3, parser.Migrations.Count);
        var configured = Assert.Single(parser.Migrations, migration => migration.Scope == scope);
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyRenameResult(0, 0));
        var service = CreateService(repository, parser);

        var migration = await service.RunConfigured(configured);

        Assert.Equal($"{scope}:rename-title", migration.MigrationId);
        Assert.Equal(expectedTargets, migration.WorkflowDefinitions);
        Assert.Single(parser.WorkflowDefinitions[scope].Migrations);
        Assert.Empty(parser.WorkflowDefinitions["Project-Sibling"].Migrations);
    }

    [Fact]
    public async Task RunConfigured_InheritedMigrationKeepsSourceScopeAndRunsOnceForAllTargets()
    {
        var parser = CreateInheritedMigrationParser();
        var configured = Assert.Single(parser.Migrations, migration => migration.Scope == "Project-Base");
        var repository = new Mock<IMigrationRepository>();
        Migration? stored = null;
        repository.Setup(value => value.GetByMigrationId(configured.MigrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .Callback<Migration, CancellationToken>((migration, _) => stored = migration)
            .Returns(Task.CompletedTask);
        repository.Setup(value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyRenameResult(4, 4));
        var service = CreateService(repository, parser);

        var migration = await service.RunConfigured(configured);
        var repeated = await service.RunConfigured(configured);

        Assert.Same(migration, repeated);
        Assert.Equal("Project-Base", migration.Scope);
        Assert.Equal("Project-Base:rename-title", migration.MigrationId);
        Assert.Equal(["Project-Base", "Project-Child", "Project-Leaf", "Project-Sibling"],
            migration.WorkflowDefinitions);
        Assert.Equal(MigrationStatus.Finished, migration.Status);
        repository.Verify(value => value.Create(migration, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.RenamePropertyValues(migration, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.RenameJournalPaths(migration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Project-Base",
        new[] { "Project-Added", "Project-Base", "Project-Child", "Project-Leaf", "Project-Sibling" })]
    [InlineData("Project-Child", new[] { "Project-Child", "Project-Leaf" })]
    public async Task RunConfigured_ResolvesTargetsFromTheModelAtExecution(string scope, string[] expectedTargets)
    {
        var parser = CreateInheritedMigrationParser();
        var configured = Assert.Single(parser.Migrations, migration => migration.Scope == scope);
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyRenameResult(0, 0));
        var service = CreateService(repository, parser);
        parser.WorkflowDefinitions.Add("Project-Added", new WorkflowDefinition
        {
            Name = "Project-Added",
            InheritsFrom = "Project-Base",
            Parent = parser.WorkflowDefinitions["Project-Base"],
            Properties = [new PropertyDefinition { Name = "ProjectTitle", Type = "String" }]
        });

        var migration = await service.RunConfigured(configured);

        Assert.Equal(expectedTargets, migration.WorkflowDefinitions);
        repository.Verify(value => value.Create(migration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunConfigured_ValidatesEveryDescendantBeforeWriting(bool containsOldProperty)
    {
        var parser = CreateInheritedMigrationParser();
        var leaf = parser.WorkflowDefinitions["Project-Leaf"];
        leaf.Properties.Clear();
        if (containsOldProperty)
            leaf.Properties.Add(new PropertyDefinition { Name = "Title", Type = "String" });
        var configured = Assert.Single(parser.Migrations, migration => migration.Scope == "Project-Base");
        var repository = new Mock<IMigrationRepository>();
        var service = CreateService(repository, parser);

        var error = await Assert.ThrowsAsync<MigrationValidationException>(() => service.RunConfigured(configured));

        Assert.Equal("MigrationInvalidModelState", error.Code);
        Assert.Contains("Workflow 'Project-Leaf'", error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(value => value.RenameJournalPaths(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunConfigured_RejectsOverlapWithAnUnfinishedDescendantMigration()
    {
        var parser = CreateInheritedMigrationParser();
        var configured = Assert.Single(parser.Migrations, migration => migration.Scope == "Project-Base");
        var existing = ReadyMigration();
        existing.WorkflowDefinitions = ["Project-Leaf"];
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>())).ReturnsAsync([existing]);
        var service = CreateService(repository, parser);

        var error = await Assert.ThrowsAsync<MigrationValidationException>(() => service.RunConfigured(configured));

        Assert.Equal("MigrationPropertyOverlap", error.Code);
        Assert.Contains("Project-Leaf.Title", error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ModelParser_DoesNotLoadMigrationsFromCommon()
    {
        var parser = new ModelParser(new DictionaryProvider(new Dictionary<string, string>
        {
            ["Common/Migrations/rename-title.yaml"] =
                "kind: renameProperty\noldProperty: Title\nnewProperty: ProjectTitle"
        }));

        Assert.Empty(parser.Migrations);
    }

    [Fact]
    public async Task RunConfigured_RunsOnlyOncePerRepository()
    {
        var repository = new Mock<IMigrationRepository>();
        Migration? stored = null;
        var migrationId = "Project:rename-title";
        repository.Setup(value => value.GetByMigrationId(migrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyRenameResult(4, 4));
        repository.Setup(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .Callback<Migration, CancellationToken>((migration, _) => stored = migration)
            .Returns(Task.CompletedTask);
        var parser = CreateConfiguredParser();
        var service = CreateService(repository, parser);
        var configured = Assert.Single(parser.Migrations, migration => migration.MigrationId == migrationId);

        var first = await service.RunConfigured(configured);
        var second = await service.RunConfigured(configured);

        Assert.Same(first, second);
        Assert.Equal(MigrationStatus.Finished, second.Status);
        Assert.Equal(migrationId, second.MigrationId);
        Assert.Equal("Project", second.Scope);
        Assert.Equal(["Project"], second.WorkflowDefinitions);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.RenamePropertyValues(first, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.RenameJournalPaths(first, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunConfigured_SkipsAnExistingMigrationIdWithoutResolvingTargets()
    {
        var parser = CreateConfiguredParser();
        var configured = Assert.Single(parser.Migrations);
        configured.OldProperty = "";
        configured.NewProperty = "";
        parser.WorkflowDefinitions.Clear();
        var existing = ReadyMigration();
        existing.MigrationId = "Project:rename-title";
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetByMigrationId(existing.MigrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var service = CreateService(repository, parser);

        var result = await service.RunConfigured(configured);

        Assert.Same(existing, result);
        Assert.Equal(["Project"], result.WorkflowDefinitions);
        repository.Verify(value => value.GetAll(It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static MigrationService CreateService(Mock<IMigrationRepository> repository,
        ModelParser? parser = null)
        => new(new ModelService(parser ?? CreateParser()), repository.Object);

    private static ConfiguredMigration CreateConfiguredMigration(string scope) => new()
    {
        Scope = scope,
        Name = "rename-title",
        Kind = MigrationKind.RenameProperty,
        OldProperty = "Title",
        NewProperty = "ProjectTitle"
    };

    private static Migration ReadyMigration() => new()
    {
        MigrationId = "migration-id",
        Scope = "Project",
        Kind = MigrationKind.RenameProperty,
        Status = MigrationStatus.Failed,
        WorkflowDefinitions = ["Project"],
        OldProperty = "Title",
        NewProperty = "ProjectTitle",
        RequestedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static ModelParser CreateParser(params string[] projectProperties)
    {
        if (projectProperties.Length == 0)
            projectProperties = ["ProjectTitle"];
        var properties = string.Join('\n', projectProperties.Select(property =>
            $"  - name: {property}\n    type: String"));

        return new ModelParser(new DictionaryProvider(new Dictionary<string, string>
        {
            ["Projects/Project/Entity.yaml"] = $"""
                                                name: Project
                                                titlePlural: Projects
                                                properties:
                                                {properties}
                                                """,
            ["Courses/Course/Entity.yaml"] = """
                                             name: Course
                                             titlePlural: Courses
                                             properties:
                                               - name: ProjectTitle
                                                 type: String
                                             """
        }));
    }

    private static ModelParser CreateConfiguredParser()
    {
        var files = new Dictionary<string, string>
        {
            ["Projects/Project/Entity.yaml"] = """
                                               name: Project
                                               titlePlural: Projects
                                               properties:
                                                 - name: ProjectTitle
                                                   type: String
                                               """,
            ["Projects/Project/Migrations/rename-title.yaml"] = """
                                                                kind: renameProperty
                                                                oldProperty: Title
                                                                newProperty: ProjectTitle
                                                                """
        };
        files["Courses/Course/Entity.yaml"] = """
                                              name: Course
                                              titlePlural: Courses
                                              properties:
                                                - name: ProjectTitle
                                                  type: String
                                              """;
        return new ModelParser(new DictionaryProvider(files));
    }

    private static ModelParser CreateInheritedMigrationParser()
    {
        var files = new Dictionary<string, string>
        {
            // Read descendants first to exercise target resolution after the entire hierarchy is loaded.
            ["Projects/Project-Leaf/Entity.yaml"] = "name: Project-Leaf\ninheritsFrom: Project-Child",
            ["Projects/Project-Child/Entity.yaml"] = "name: Project-Child\ninheritsFrom: Project-Base",
            ["Projects/Project-Sibling/Entity.yaml"] = "name: Project-Sibling\ninheritsFrom: Project-Base",
            ["Projects/Project-Base/Entity.yaml"] = """
                                                    name: Project-Base
                                                    titlePlural: Projects
                                                    properties:
                                                      - name: ProjectTitle
                                                        type: String
                                                    """,
            ["Courses/Course/Entity.yaml"] = """
                                             name: Course
                                             titlePlural: Courses
                                             properties:
                                               - name: ProjectTitle
                                                 type: String
                                             """
        };
        foreach (var scope in new[] { "Project-Base", "Project-Child", "Project-Leaf" })
            files[$"Projects/{scope}/Migrations/rename-title.yaml"] =
                "kind: renameProperty\noldProperty: Title\nnewProperty: ProjectTitle";
        return new ModelParser(new DictionaryProvider(files));
    }
}