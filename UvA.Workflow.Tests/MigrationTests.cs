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
        Assert.False(document.Contains("Definition"));
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

    [Fact]
    public void ModelParser_ReadsMigrationsFromWorkflowFolder()
    {
        var parser = CreateConfiguredParser();

        var migration = Assert.Single(parser.Migrations);
        Assert.Equal("Project:rename-title", migration.MigrationId);
        Assert.Equal(MigrationKind.RenameProperty, migration.Kind);
        Assert.Equal("Title", migration.OldProperty);
        Assert.Equal("ProjectTitle", migration.NewProperty);
    }

    [Fact]
    public async Task RunConfigured_RunsOnlyOncePerRepository()
    {
        var repository = new Mock<IMigrationRepository>();
        Migration? stored = null;
        repository.Setup(value => value.GetByMigrationId("Project:rename-title", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.CopyPropertyValues(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(4, 4));
        repository.Setup(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .Callback<Migration, CancellationToken>((migration, _) => stored = migration)
            .Returns(Task.CompletedTask);
        var parser = CreateConfiguredParser();
        var service = CreateService(repository, parser);

        var first = await service.RunConfigured(Assert.Single(parser.Migrations));
        var second = await service.RunConfigured(Assert.Single(parser.Migrations));

        Assert.Same(first, second);
        Assert.Equal(MigrationStatus.Finished, second.Status);
        Assert.Equal("configuration", second.RequestedBy);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(value => value.CopyPropertyValues(It.IsAny<Migration>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(value => value.CopyPropertyValues(It.IsAny<Migration>(),
            It.IsAny<CancellationToken>()), Times.Never);
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

    private static ModelParser CreateConfiguredParser()
        => new(new DictionaryProvider(new Dictionary<string, string>
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
        }));
}