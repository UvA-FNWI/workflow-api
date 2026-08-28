using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Migrations;

public sealed class MigrationValidationException(string code, string message)
    : WorkflowException(code, message);

public sealed class MigrationNotFoundException(string id)
    : WorkflowException("MigrationNotFound", $"Migration '{id}' does not exist");

public sealed class InvalidMigrationStateException(string message)
    : WorkflowException("InvalidMigrationState", message);