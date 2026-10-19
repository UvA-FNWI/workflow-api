using Microsoft.Extensions.DependencyInjection;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Migrations;
using UvA.Workflow.Migrations;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class ConfiguredMigrationRunnerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task Run_EnforcesSharedRetryBudget(int previousAttempts)
    {
        var parser = new ModelParser(new DictionaryProvider(new Dictionary<string, string>
        {
            ["Projects/Project/Entity.yaml"] = "name: Project\nproperties:\n  - name: ProjectTitle\n    type: String",
            ["Projects/Project/Migrations/rename-title.yaml"] = "oldProperty: Title\nnewProperty: ProjectTitle"
        }));
        var migration = new Migration
        {
            MigrationId = Assert.Single(parser.Migrations).MigrationId,
            Status = MigrationStatus.Failed,
            AttemptCount = previousAttempts,
            WorkflowDefinitions = ["Project"], OldProperty = "Title", NewProperty = "ProjectTitle"
        };
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetByMigrationId(migration.MigrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(migration);
        repository.Setup(value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyRenameResult(0, 0));
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddWorkflowApiCore()
            .AddScoped<IMigrationRepository>(_ => repository.Object)
            .BuildServiceProvider();
        var runner = provider.GetRequiredService<IConfiguredMigrationRunner>();

        if (previousAttempts == 0)
        {
            await runner.Run(parser);
            Assert.Equal(MigrationStatus.Finished, migration.Status);
            Assert.Equal(1, migration.AttemptCount);
        }
        else
        {
            await Assert.ThrowsAsync<MigrationRetryLimitException>(() => runner.Run(parser));
            repository.Verify(value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}