using UvA.Workflow.Infrastructure;

namespace UvA.Workflow.Migrations;

public sealed class MigrationValidationException(string code, string message)
    : WorkflowException(code, message);