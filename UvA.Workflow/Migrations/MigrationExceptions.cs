using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Migrations;

public sealed class MigrationValidationException(string code, string message)
    : WorkflowException(code, message);

public sealed class MigrationRetryLimitException(string migrationId, int attemptCount, int maxAttempts)
    : WorkflowException("MigrationRetryLimitExceeded",
        $"Migration '{migrationId}' has exhausted its retry budget ({attemptCount}/{maxAttempts} attempts). " +
        "Resolve the failure and reset the migration's saved AttemptCount before retrying.");