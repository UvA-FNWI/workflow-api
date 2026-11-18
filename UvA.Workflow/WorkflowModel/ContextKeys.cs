namespace UvA.Workflow.WorkflowModel;

public static class ContextKeys
{
    public const string Id = nameof(Id);
    public const string CurrentStep = nameof(CurrentStep);
    public const string CreateDate = nameof(CreateDate);
    public const string LastEvent = nameof(LastEvent);
    public const string WorkflowStopped = nameof(WorkflowStopped);
    public const string ValueSets = nameof(ValueSets);
    public const string Now = "now";

    internal static IReadOnlyList<string> Reserved { get; } =
        [Id, CurrentStep, CreateDate, LastEvent, WorkflowStopped, ValueSets, Now];

    public static bool IsComputed(string key) => key is LastEvent or WorkflowStopped or ValueSets or Now;
}