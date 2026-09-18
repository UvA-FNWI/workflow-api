using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Deadlines;

public record DeadlineChange(string Property, DateTimeOffset PreviousDate, DateOnly NewDate);

public record PostponeDeadlinesRequest(DeadlineChange[] Changes, string Reason);

public enum PostponementError
{
    InvalidChanges,
    MaximumExtensionExceeded
}

public record PostponeDeadlinesResult(PostponementError[] Errors, EffectResult? Effects = null);