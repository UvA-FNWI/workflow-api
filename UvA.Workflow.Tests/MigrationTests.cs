using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Migrations;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class MigrationTests
{
    [Fact]
    public void RenamePropertyDefinition_RoundTripsThroughMongoSerialization()
    {
        var migration = ReadyMigration();

        var restored = BsonSerializer.Deserialize<Migration>(migration.ToBson());

        var definition = Assert.IsType<RenamePropertyDefinition>(restored.Definition);
        Assert.Equal(["Project"], definition.WorkflowDefinitions);
        Assert.Equal("Title", definition.OldProperty);
        Assert.Equal("ProjectTitle", definition.NewProperty);
    }

    [Fact]
    public async Task CreatePropertyRename_CopiesPropertyValuesAndBecomesReadyToFinish()
    {
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.CountTargetFields(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        repository.Setup(value => value.CopyPropertyValues(It.IsAny<Migration>(), false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(3, 3));
        var service = CreateService(repository);

        var migration = await service.CreatePropertyRename(
            ["Project", "Course"], "Title", "ProjectTitle", "admin");

        Assert.Equal(MigrationStatus.ReadyToFinish, migration.Status);
        Assert.Equal(3, migration.Progress.ItemsMatched);
        Assert.Equal(3, migration.Progress.ItemsUpdated);
        var definition = Assert.IsType<RenamePropertyDefinition>(migration.Definition);
        Assert.Equal(["Project", "Course"], definition.WorkflowDefinitions);
        Assert.Equal("Title", definition.OldProperty);
        Assert.Equal("ProjectTitle", definition.NewProperty);
        repository.Verify(value => value.Create(migration, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.Update(migration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePropertyRename_AllowsPostDeploymentModel()
    {
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.CountTargetFields(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        repository.Setup(value => value.CopyPropertyValues(It.IsAny<Migration>(), false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(3, 3));
        var service = CreateService(repository, CreateParser("ProjectTitle"));

        var migration = await service.CreatePropertyRename(
            ["Project"], "Title", "ProjectTitle", "admin");

        Assert.Equal(MigrationStatus.ReadyToFinish, migration.Status);
    }

    [Fact]
    public async Task CreatePropertyRename_RejectsUnknownWorkflow()
    {
        var repository = new Mock<IMigrationRepository>();
        var service = CreateService(repository);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePropertyRename(["Unknown"], "Title", "ProjectTitle", "admin"));

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

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePropertyRename(["Project"], "Title", "ProjectTitle", "admin"));

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
        var existingDefinition = Assert.IsType<RenamePropertyDefinition>(existing.Definition);
        existingDefinition.WorkflowDefinitions = [existingWorkflow];
        existingDefinition.OldProperty = existingOldProperty;
        existingDefinition.NewProperty = existingNewProperty;
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);
        repository.Setup(value => value.CountTargetFields(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        repository.Setup(value => value.CopyPropertyValues(It.IsAny<Migration>(), false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(3, 3));
        var service = CreateService(repository);

        var migration = await service.CreatePropertyRename(
            ["Project"], "Title", "ProjectTitle", "admin");

        Assert.Equal(MigrationStatus.ReadyToFinish, migration.Status);
        repository.Verify(value => value.Create(migration, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Title", "OtherTitle")]
    [InlineData("LegacyTitle", "ProjectTitle")]
    public async Task CreatePropertyRename_RejectsOverlappingMigrationForTheSameWorkflow(
        string existingOldProperty,
        string existingNewProperty)
    {
        var repository = new Mock<IMigrationRepository>();
        var existing = ReadyMigration();
        var existingDefinition = Assert.IsType<RenamePropertyDefinition>(existing.Definition);
        existingDefinition.OldProperty = existingOldProperty;
        existingDefinition.NewProperty = existingNewProperty;
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);
        var service = CreateService(repository);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePropertyRename(["Project"], "Title", "ProjectTitle", "admin"));

        Assert.Contains("selected properties", error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Finish_RefreshesCopiedValuesAndRenamesJournalPaths()
    {
        var migration = ReadyMigration();
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetById(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(migration);
        repository.Setup(value => value.CopyPropertyValues(migration, true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyCopyResult(4, 4));
        repository.Setup(value => value.RenameJournalPaths(migration, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var service = CreateService(repository);

        var result = await service.Finish(migration.Id);

        Assert.Equal(MigrationStatus.Finished, result.Status);
        Assert.Equal(4, result.Progress.ItemsUpdated);
        Assert.Equal(7, result.Progress.Details["JournalEntriesUpdated"]);
        Assert.NotNull(result.FinishedAt);
        repository.Verify(value => value.Update(migration, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Revert_RemovesTheCopiedTargetProperty()
    {
        var migration = ReadyMigration();
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetById(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(migration);
        var service = CreateService(repository);

        var result = await service.Revert(migration.Id);

        Assert.Equal(MigrationStatus.Reverted, result.Status);
        repository.Verify(value => value.RemoveTargetFields(migration,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePropertyRename_RejectsAnExistingTargetField()
    {
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Migration>());
        repository.Setup(value => value.CountTargetFields(It.IsAny<Migration>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = CreateService(repository);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePropertyRename(["Project"], "Title", "ProjectTitle", "admin"));

        Assert.Contains("already contain", error.Message);
        repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static MigrationService CreateService(Mock<IMigrationRepository> repository,
        ModelParser? parser = null)
        => new(new ModelService(parser ?? CreateParser()), repository.Object);

    private static Migration ReadyMigration() => new()
    {
        Id = "migration-id",
        Kind = MigrationKind.RenameProperty,
        Status = MigrationStatus.ReadyToFinish,
        Definition = new RenamePropertyDefinition
        {
            WorkflowDefinitions = ["Project"],
            OldProperty = "Title",
            NewProperty = "ProjectTitle"
        },
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
}