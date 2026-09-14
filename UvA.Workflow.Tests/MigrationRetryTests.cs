using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using UvA.Workflow.Migrations;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class MigrationRetryTests
{
    [Fact]
    public async Task RetryBudget_SurvivesRestartsAndRequiresAManualReset()
    {
        const int maxAttempts = 3;
        var fixture = new Fixture();
        fixture.Repository
            .Setup(value => value.RenameJournalPaths(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Journal write failed"));

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service().RunConfigured(fixture.Configured));
            Assert.Equal(attempt, fixture.Stored.AttemptCount);
            Assert.Equal(MigrationStatus.Failed, fixture.Stored.Status);
        }

        for (var restart = 0; restart < 2; restart++)
            await Assert.ThrowsAsync<MigrationRetryLimitException>(() =>
                fixture.Service().RunConfigured(fixture.Configured));

        Assert.Equal("Journal write failed", fixture.Stored.Error);
        Assert.Equal(maxAttempts, fixture.Stored.AttemptCount);
        fixture.Repository.Verify(
            value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Exactly(maxAttempts));
        fixture.Repository.Verify(
            value => value.RenameJournalPaths(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Exactly(maxAttempts));
        fixture.Repository.Verify(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.Repository
            .Setup(value => value.RenameJournalPaths(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        var reset = fixture.Stored;
        reset.AttemptCount = 0;
        fixture.Save(reset);
        var resumed = await fixture.Service().RunConfigured(fixture.Configured);
        Assert.Equal(MigrationStatus.Finished, resumed.Status);
        Assert.Equal(reset.Id, resumed.Id);
        Assert.Equal(1, fixture.Stored.AttemptCount);
    }

    [Theory]
    [InlineData(2, MigrationStatus.Finished)]
    [InlineData(3, MigrationStatus.Failed)]
    public async Task InterruptedAttempt_ConsumesBudget(int attempts, MigrationStatus expectedStatus)
    {
        var fixture = new Fixture();
        fixture.Save(new Migration
        {
            MigrationId = fixture.Configured.MigrationId,
            Status = MigrationStatus.Applying,
            AttemptCount = attempts,
            OldProperty = "Title",
            NewProperty = "ProjectTitle",
            WorkflowDefinitions = ["Project"]
        });

        if (attempts == 3)
        {
            await Assert.ThrowsAsync<MigrationRetryLimitException>(() =>
                fixture.Service().RunConfigured(fixture.Configured));
            fixture.Repository.Verify(
                value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.Contains("exhausted", fixture.Stored.Error);
        }
        else
        {
            await fixture.Service().RunConfigured(fixture.Configured);
        }

        Assert.Equal(3, fixture.Stored.AttemptCount);
        Assert.Equal(expectedStatus, fixture.Stored.Status);
    }

    [Fact]
    public async Task FinishedMigration_IsSkippedEvenWhenBudgetIsExhausted()
    {
        var fixture = new Fixture();
        fixture.Save(new Migration
            { MigrationId = fixture.Configured.MigrationId, Status = MigrationStatus.Finished, AttemptCount = 3 });

        var migration = await fixture.Service().RunConfigured(fixture.Configured);

        Assert.Equal(MigrationStatus.Finished, migration.Status);
        fixture.Repository.Verify(value => value.Update(It.IsAny<Migration>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Repository.Verify(
            value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_CancelsWorkAndSavesFailureWithAnIndependentToken(bool duringJournals)
    {
        var fixture = new Fixture();
        var operationStopped = false;
        using var timeout = new CancellationTokenSource();
        CancellationToken operationToken = default;

        async Task Stall(CancellationToken ct)
        {
            operationToken = ct;
            Assert.Equal(1, fixture.Stored.AttemptCount);
            Assert.Equal(MigrationStatus.Applying, fixture.Stored.Status);
            try
            {
                // Signal deadline expiry directly, without waiting for the fixed five-minute timer.
                timeout.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                operationStopped = true;
            }
        }

        if (duringJournals)
            fixture.Repository.Setup(value =>
                    value.RenameJournalPaths(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
                .Returns(async (Migration _, CancellationToken ct) =>
                {
                    await Stall(ct);
                    return 0L;
                });
        else
            fixture.Repository.Setup(value =>
                    value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
                .Returns(async (Migration _, CancellationToken ct) =>
                {
                    await Stall(ct);
                    return new PropertyRenameResult(0, 0);
                });

        var error = await Assert.ThrowsAsync<TimeoutException>(() => fixture.Service()
            .RunConfiguredAttempt(fixture.Configured, timeout.Token, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Contains("attempt timeout", error.Message);
        Assert.True(operationStopped);
        Assert.True(operationToken.IsCancellationRequested);
        Assert.Equal(MigrationStatus.Failed, fixture.Stored.Status);
        Assert.Equal(error.Message, fixture.Stored.Error);
        Assert.Null(fixture.Stored.FinishedAt);
        Assert.Equal(1, fixture.Stored.AttemptCount);
        if (!duringJournals)
            fixture.Repository.Verify(
                value => value.RenameJournalPaths(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsTimeoutAndStillSavesFailure()
    {
        var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        fixture.Repository.Setup(value =>
                value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .Returns((Migration _, CancellationToken ct) =>
            {
                caller.Cancel();
                return Task.FromCanceled<PropertyRenameResult>(ct);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service().RunConfigured(fixture.Configured, caller.Token));

        Assert.Equal(MigrationStatus.Failed, fixture.Stored.Status);
        Assert.DoesNotContain("timeout", fixture.Stored.Error!);
        Assert.Equal(1, fixture.Stored.AttemptCount);
    }

    [Fact]
    public async Task FailureSaveError_PreservesOriginalErrorAndDurableAttemptCount()
    {
        var fixture = new Fixture();
        fixture.Repository
            .Setup(value => value.RenameJournalPaths(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Journal write failed"));
        fixture.Repository.Setup(value => value.Update(
                It.Is<Migration>(migration => migration.Status == MigrationStatus.Failed),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Failure save failed"));

        var error = await Assert.ThrowsAsync<AggregateException>(() =>
            fixture.Service().RunConfigured(fixture.Configured));

        Assert.Equal(new[] { "Journal write failed", "Failure save failed" },
            error.InnerExceptions.Select(value => value.Message));
        Assert.Equal(MigrationStatus.Applying, fixture.Stored.Status);
        Assert.Equal(1, fixture.Stored.AttemptCount);
    }

    [Fact]
    public void LegacyRecordWithoutAttemptCount_StartsAtZero()
    {
        var document = new Migration { AttemptCount = 2 }.ToBsonDocument();
        document.Remove(nameof(Migration.AttemptCount));
        Assert.Equal(0, BsonSerializer.Deserialize<Migration>(document).AttemptCount);
    }

    private sealed class Fixture
    {
        public Mock<IMigrationRepository> Repository { get; } = new();
        private byte[]? _stored;
        public Migration Stored => BsonSerializer.Deserialize<Migration>(_stored!);
        public void Save(Migration migration) => _stored = migration.ToBson();

        public ConfiguredMigration Configured { get; } = new()
        {
            Scope = "Project", Name = "rename-title", OldProperty = "Title", NewProperty = "ProjectTitle"
        };

        public Fixture()
        {
            Repository.Setup(value => value.GetByMigrationId(Configured.MigrationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _stored == null ? null : Stored);
            Repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<Migration>());
            Repository.Setup(value => value.Create(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
                .Callback<Migration, CancellationToken>((migration, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Save(migration);
                })
                .Returns(Task.CompletedTask);
            Repository.Setup(value => value.Update(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
                .Callback<Migration, CancellationToken>((migration, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Save(migration);
                })
                .Returns(Task.CompletedTask);
            Repository.Setup(value => value.RenamePropertyValues(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
                .Callback(() => Assert.Equal(MigrationStatus.Applying, Stored.Status))
                .ReturnsAsync(new PropertyRenameResult(0, 0));
            Repository.Setup(value => value.RenameJournalPaths(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);
        }

        public MigrationService Service()
            => new(new ModelService(new ModelParser(new DictionaryProvider(new Dictionary<string, string>
            {
                ["Projects/Project/Entity.yaml"] =
                    "name: Project\nproperties:\n  - name: ProjectTitle\n    type: String"
            }))), Repository.Object);
    }
}