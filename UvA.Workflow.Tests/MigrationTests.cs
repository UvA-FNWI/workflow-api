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
        Assert.False(document.Contains("WorkflowDefinition"));
        Assert.Equal(BsonType.Array, document["WorkflowDefinitions"].BsonType);
        Assert.Equal("Title", document["OldProperty"].AsString);
        Assert.Equal(["Project"], restored.WorkflowDefinitions);
        Assert.Equal("Title", restored.OldProperty);
        Assert.Equal("ProjectTitle", restored.NewProperty);
    }

    [Fact]
    public async Task CreatePropertyRename_RunsToCompletionImmediately()
    {
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.CopyPropertyValues(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(3, 3));
        repository.Setup(value => value.RenameJournalPaths(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var service = CreateService(repository);

        var migration = await service.CreatePropertyRename(
            ["Project", "Course"], "Title", "ProjectTitle", "admin");

        Assert.Equal(MigrationStatus.Finished, migration.Status);
        Assert.Equal(3, migration.ItemsMatched);
        Assert.Equal(3, migration.ItemsUpdated);
        Assert.Equal(["Project", "Course"], migration.WorkflowDefinitions);
        Assert.Equal("Title", migration.OldProperty);
        Assert.Equal("ProjectTitle", migration.NewProperty);
        Assert.Equal(2, migration.JournalEntriesUpdated);
        Assert.NotNull(migration.FinishedAt);
        repository.Verify(value => value.Create(migration, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.Update(migration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePropertyRename_AllowsPostDeploymentModel()
    {
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.CopyPropertyValues(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(3, 3));
        var service = CreateService(repository, CreateParser("ProjectTitle"));

        var migration = await service.CreatePropertyRename(
            ["Project"], "Title", "ProjectTitle", "admin");

        Assert.Equal(MigrationStatus.Finished, migration.Status);
    }

    [Fact]
    public async Task CreatePropertyRename_RejectsUnknownWorkflow()
    {
        var repository = new Mock<IMigrationRepository>();
        var service = CreateService(repository);

        var error = await Assert.ThrowsAsync<MigrationValidationException>(() =>
            service.CreatePropertyRename(["Unknown"], "Title", "ProjectTitle", "admin"));

        Assert.Equal("MigrationUnknownWorkflow", error.Code);
        Assert.Equal("Unknown workflow 'Unknown'", error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreatePropertyRename_RejectsModelContainingBothOrNeitherProperty(bool containsBoth)
    {
        var repository = new Mock<IMigrationRepository>();
        var parser = containsBoth
            ? CreateParser("Title", "ProjectTitle")
            : CreateParser("Code");
        var service = CreateService(repository, parser);

        var error = await Assert.ThrowsAsync<MigrationValidationException>(() =>
            service.CreatePropertyRename(["Project"], "Title", "ProjectTitle", "admin"));

        Assert.Equal("MigrationInvalidModelState", error.Code);
        Assert.Contains("must contain exactly one", error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Project", "Code", "ProjectCode")]
    [InlineData("Course", "Title", "ProjectTitle")]
    public async Task CreatePropertyRename_AllowsMigrationWithoutWorkflowAndPropertyOverlap(
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
        repository.Setup(value => value.CopyPropertyValues(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(3, 3));
        var service = CreateService(repository);

        var migration = await service.CreatePropertyRename(
            ["Project"], "Title", "ProjectTitle", "admin");

        Assert.Equal(MigrationStatus.Finished, migration.Status);
        repository.Verify(value => value.Create(migration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Title", "OtherTitle", "Project.Title")]
    [InlineData("LegacyTitle", "ProjectTitle", "Project.ProjectTitle")]
    public async Task CreatePropertyRename_RejectsOverlappingMigrationForTheSameWorkflow(
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
            service.CreatePropertyRename(["Project"], "Title", "ProjectTitle", "admin"));

        Assert.Equal("MigrationPropertyOverlap", error.Code);
        Assert.Contains(expectedConflict, error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreatePropertyRename_RequiresWorkflow(string? workflow)
    {
        var repository = new Mock<IMigrationRepository>();
        var service = CreateService(repository);

        var error = await Assert.ThrowsAsync<MigrationValidationException>(() =>
            service.CreatePropertyRename([workflow!], "Title", "ProjectTitle", "admin"));

        Assert.Equal("MigrationWorkflowRequired", error.Code);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ModelParser_ReadsMigrationsFromWorkflowFolder()
    {
        var parser = CreateConfiguredParser();

        var migration = Assert.Single(parser.Migrations);
        Assert.Equal("Project:rename-title", migration.MigrationId);
        Assert.Equal(["Project"], migration.WorkflowDefinitions);
        Assert.Equal(MigrationKind.RenameProperty, migration.Kind);
        Assert.Equal("Title", migration.OldProperty);
        Assert.Equal("ProjectTitle", migration.NewProperty);
    }

    [Theory]
    [InlineData("Title", true)]
    [InlineData("ProjectTitle", true)]
    [InlineData("Code", false)]
    public void ModelParser_CommonGeneratesApplicableWorkflowDefinitions(string courseProperty, bool includesCourse)
    {
        var parser = CreateConfiguredParser(includeCommon: true, courseProperty: courseProperty);

        Assert.Equal(["Common:rename-title", "Project:rename-title"],
            parser.Migrations.Select(migration => migration.MigrationId).Order(StringComparer.Ordinal));
        var common = Assert.Single(parser.Migrations, migration => migration.Scope == "Common");
        Assert.Equal(includesCourse ? ["Course", "Project"] : new[] { "Project" }, common.WorkflowDefinitions);
        Assert.Equal(MigrationKind.RenameProperty, common.Kind);
        Assert.Equal("Title", common.OldProperty);
        Assert.Equal("ProjectTitle", common.NewProperty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunConfigured_RunsOnlyOncePerRepository(bool common)
    {
        var repository = new Mock<IMigrationRepository>();
        Migration? stored = null;
        var migrationId = common ? "Common:rename-title" : "Project:rename-title";
        repository.Setup(value => value.GetByMigrationId(migrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.CopyPropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(4, 4));
        repository.Setup(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .Callback<Migration, CancellationToken>((migration, _) => stored = migration)
            .Returns(Task.CompletedTask);
        var parser = CreateConfiguredParser(includeCommon: common);
        var service = CreateService(repository, parser);
        var configured = Assert.Single(parser.Migrations, migration => migration.MigrationId == migrationId);

        var first = await service.RunConfigured(configured);
        var second = await service.RunConfigured(configured);

        Assert.Same(first, second);
        Assert.Equal(MigrationStatus.Finished, second.Status);
        Assert.Equal("configuration", second.RequestedBy);
        Assert.Equal(migrationId, second.MigrationId);
        Assert.Equal(common ? ["Course", "Project"] : new[] { "Project" }, second.WorkflowDefinitions);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.CopyPropertyValues(first, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.RenameJournalPaths(first, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunConfigured_CommonRejectsWorkflowContainingBothPropertiesBeforeWriting()
    {
        var parser = CreateConfiguredParser(includeCommon: true);
        parser.WorkflowDefinitions["Course"].Properties.Add(new PropertyDefinition { Name = "Title", Type = "String" });
        var repository = new Mock<IMigrationRepository>();
        var service = CreateService(repository, parser);
        var configured = Assert.Single(parser.Migrations, migration => migration.Scope == "Common");

        var error = await Assert.ThrowsAsync<MigrationValidationException>(() => service.RunConfigured(configured));

        Assert.Equal("MigrationInvalidModelState", error.Code);
        Assert.Contains("Workflow 'Course'", error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(value => value.CopyPropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunConfigured_CommonSkipsWorkflowsWithNeitherProperty(bool changesAfterParsing)
    {
        var parser = CreateConfiguredParser(includeCommon: true,
            courseProperty: changesAfterParsing ? "ProjectTitle" : "Code");
        if (changesAfterParsing)
            parser.WorkflowDefinitions["Course"].Properties.Clear();
        var repository = new Mock<IMigrationRepository>();
        var unrelated = ReadyMigration();
        unrelated.WorkflowDefinitions = ["Course"];
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>())).ReturnsAsync([unrelated]);
        repository.Setup(value => value.CopyPropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(2, 2));
        var service = CreateService(repository, parser);
        var configured = Assert.Single(parser.Migrations, migration => migration.Scope == "Common");

        var result = await service.RunConfigured(configured);

        Assert.Equal(MigrationStatus.Finished, result.Status);
        Assert.Equal(["Project"], result.WorkflowDefinitions);
        repository.Verify(value => value.CopyPropertyValues(result, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.RenameJournalPaths(result, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunConfigured_CommonWithNoApplicableWorkflowsFinishesWithoutDataWrites()
    {
        var parser = CreateConfiguredParser(includeCommon: true);
        foreach (var definition in parser.WorkflowDefinitions.Values)
            definition.Properties.Clear();
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Migration>());
        var service = CreateService(repository, parser);
        var configured = Assert.Single(parser.Migrations, migration => migration.Scope == "Common");

        var result = await service.RunConfigured(configured);

        Assert.Equal(MigrationStatus.Finished, result.Status);
        Assert.Empty(result.WorkflowDefinitions);
        Assert.Equal(0, result.ItemsMatched);
        Assert.Equal(0, result.ItemsUpdated);
        Assert.Equal(0, result.JournalEntriesUpdated);
        repository.Verify(value => value.Create(result, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.Update(result, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.CopyPropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(value => value.RenameJournalPaths(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunConfigured_SkipsAnExistingMigrationId()
    {
        var parser = CreateConfiguredParser();
        var configured = Assert.Single(parser.Migrations);
        configured.OldProperty = "";
        configured.NewProperty = "";
        var existing = ReadyMigration();
        existing.MigrationId = "Project:rename-title";
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetByMigrationId(existing.MigrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var service = CreateService(repository, parser);

        var result = await service.RunConfigured(configured);

        Assert.Same(existing, result);
        repository.Verify(value => value.GetAll(It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(value => value.CopyPropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static MigrationService CreateService(Mock<IMigrationRepository> repository,
        ModelParser? parser = null)
        => new(new ModelService(parser ?? CreateParser()), repository.Object);

    private static Migration ReadyMigration() => new()
    {
        MigrationId = "migration-id",
        Kind = MigrationKind.RenameProperty,
        Status = MigrationStatus.Failed,
        WorkflowDefinitions = ["Project"],
        OldProperty = "Title",
        NewProperty = "ProjectTitle",
        RequestedBy = "admin",
        RequestedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static ModelParser CreateParser(params string[] projectProperties)
    {
        if (projectProperties.Length == 0)
            projectProperties = ["Title"];
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
                                               - name: Title
                                                 type: String
                                             """
        }));
    }

    private static ModelParser CreateConfiguredParser(bool includeCommon = false,
        string courseProperty = "ProjectTitle")
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
        files["Courses/Course/Entity.yaml"] = $"""
                                               name: Course
                                               titlePlural: Courses
                                               properties:
                                                 - name: {courseProperty}
                                                   type: String
                                               """;
        if (includeCommon)
            files["Common/Migrations/rename-title.yaml"] = files["Projects/Project/Migrations/rename-title.yaml"];
        return new ModelParser(new DictionaryProvider(files));
    }
}