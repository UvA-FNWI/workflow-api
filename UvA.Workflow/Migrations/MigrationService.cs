using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Migrations;

public class MigrationService(
    ModelService modelService,
    IMigrationRepository migrationRepository)
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailureSaveTimeout = TimeSpan.FromSeconds(10);

    public Task<IReadOnlyList<Migration>> GetAll(CancellationToken ct = default)
        => migrationRepository.GetAll(ct);

    public async Task<Migration> RunConfigured(ConfiguredMigration configured, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(AttemptTimeout);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        Migration? migration = null;
        try
        {
            migration = await GetOrCreateMigration(configured, attempt.Token);
            if (migration.Status == MigrationStatus.Finished)
                return migration;

            return await Execute(migration, attempt.Token);
        }
        catch (Exception exception)
        {
            var failure = exception is OperationCanceledException && timeout.IsCancellationRequested &&
                          !ct.IsCancellationRequested
                ? Timeout(configured.MigrationId, exception)
                : exception;
            if (migration != null)
            {
                try
                {
                    await SaveFailure(migration, failure);
                }
                catch (Exception saveException)
                {
                    throw new AggregateException(
                        $"Migration '{migration.MigrationId}' failed and its failure could not be saved",
                        failure, saveException);
                }
            }

            if (failure != exception)
                throw failure;
            throw;
        }
    }

    private async Task<Migration> GetOrCreateMigration(ConfiguredMigration configured, CancellationToken ct)
    {
        var existing = await migrationRepository.GetByMigrationId(configured.MigrationId, ct);
        ct.ThrowIfCancellationRequested();
        if (existing?.Status == MigrationStatus.Finished)
            return existing;

        if (existing != null && existing.AttemptCount >= MaxAttempts)
        {
            var error = new MigrationRetryLimitException(existing.MigrationId, existing.AttemptCount, MaxAttempts);
            if (existing.Status == MigrationStatus.Applying)
                await SaveFailure(existing, error);
            throw error;
        }

        // Resume the saved operation, including its original targets, after a failure or interrupted startup.
        if (existing != null)
            return existing;

        var migration = new Migration
        {
            MigrationId = configured.MigrationId,
            Scope = configured.Scope,
            Kind = configured.Kind,
            WorkflowDefinitions = ResolveWorkflowDefinitions(configured.Scope),
            OldProperty = configured.OldProperty,
            NewProperty = configured.NewProperty
        };
        await Prepare(migration, ct);
        await migrationRepository.Create(migration, ct);
        return migration;
    }

    private async Task Prepare(
        Migration migration,
        CancellationToken ct)
    {
        if (migration.Kind != MigrationKind.RenameProperty)
            throw new MigrationValidationException("UnsupportedMigrationKind",
                $"Migration kind '{migration.Kind}' is not supported");
        ValidatePropertyName(migration.OldProperty, nameof(Migration.OldProperty));
        ValidatePropertyName(migration.NewProperty, nameof(Migration.NewProperty));
        if (migration.OldProperty == migration.NewProperty)
            throw new MigrationValidationException("MigrationPropertiesMustDiffer",
                "The old and new property names must be different");

        foreach (var workflow in migration.WorkflowDefinitions)
        {
            if (!modelService.WorkflowDefinitions.TryGetValue(workflow, out var definition))
                throw new MigrationValidationException("MigrationUnknownWorkflow",
                    $"Unknown workflow '{workflow}'");

            var hasOldProperty = definition.Properties.Contains(migration.OldProperty);
            var hasNewProperty = definition.Properties.Contains(migration.NewProperty);
            if (hasOldProperty || !hasNewProperty)
                throw new MigrationValidationException("MigrationInvalidModelState",
                    $"Workflow '{workflow}' must contain '{migration.NewProperty}' and must not contain '{migration.OldProperty}'");
        }

        var requestedProperties = new HashSet<string>([migration.OldProperty, migration.NewProperty],
            StringComparer.Ordinal);
        var active = await migrationRepository.GetAll(ct);
        var conflictingProperties = active
            .Where(value => value.MigrationId != migration.MigrationId && value.Status != MigrationStatus.Finished)
            .SelectMany(activeMigration => activeMigration.WorkflowDefinitions
                .Intersect(migration.WorkflowDefinitions, StringComparer.Ordinal)
                .SelectMany(workflow => new[] { activeMigration.OldProperty, activeMigration.NewProperty }
                    .Intersect(requestedProperties, StringComparer.Ordinal)
                    .Select(property => $"{workflow}.{property}")))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (conflictingProperties.Length > 0)
            throw new MigrationValidationException("MigrationPropertyOverlap",
                $"The following workflow properties already have an unfinished migration: {string.Join(", ", conflictingProperties)}");

        var now = DateTime.UtcNow;
        migration.Status = MigrationStatus.Applying;
        migration.RequestedAt = now;
        migration.UpdatedAt = now;
    }

    private async Task<Migration> Execute(Migration migration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        migration.Status = MigrationStatus.Applying;
        migration.AttemptCount++;
        migration.Error = null;
        migration.FinishedAt = null;
        migration.UpdatedAt = DateTime.UtcNow;

        // Persist the attempt before changing data so process restarts cannot reset the budget.
        await migrationRepository.Update(migration, ct);
        ct.ThrowIfCancellationRequested();
        // Both operations only match the old name, so completed writes are no-ops on retry.
        var result = await migrationRepository.RenamePropertyValues(migration, ct);
        migration.ItemsMatched += result.InstancesMatched;
        migration.ItemsUpdated += result.InstancesUpdated;
        ct.ThrowIfCancellationRequested();
        migration.JournalEntriesUpdated += await migrationRepository.RenameJournalPaths(migration, ct);
        ct.ThrowIfCancellationRequested();

        migration.Status = MigrationStatus.Finished;
        migration.FinishedAt = migration.UpdatedAt = DateTime.UtcNow;
        await migrationRepository.Update(migration, ct);
        return migration;
    }

    private async Task SaveFailure(Migration migration, Exception exception)
    {
        migration.Status = MigrationStatus.Failed;
        migration.FinishedAt = null;
        migration.Error = exception.Message;
        migration.UpdatedAt = DateTime.UtcNow;
        // The attempt (or caller) may already be canceled. Bound cleanup independently.
        using var cleanup = new CancellationTokenSource(FailureSaveTimeout);
        await migrationRepository.Update(migration, cleanup.Token);
    }

    private static TimeoutException Timeout(string migrationId, Exception exception)
        => new($"Migration '{migrationId}' exceeded its attempt timeout of {AttemptTimeout}", exception);

    private string[] ResolveWorkflowDefinitions(string scope)
    {
        if (!modelService.WorkflowDefinitions.ContainsKey(scope))
            throw new MigrationValidationException("MigrationUnknownWorkflow", $"Unknown workflow '{scope}'");

        return modelService.WorkflowDefinitions.Values
            .Where(definition => IsInScope(definition, scope))
            .Select(definition => definition.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsInScope(WorkflowDefinition definition, string scope)
    {
        for (var current = definition; current != null; current = current.Parent)
            if (current.Name == scope)
                return true;

        return false;
    }

    private static void ValidatePropertyName(string property, string field)
    {
        if (string.IsNullOrWhiteSpace(property) || property.Contains('.') || property.StartsWith('$'))
            throw new MigrationValidationException("InvalidMigrationProperty",
                $"{field} must be a top-level property name");
    }
}