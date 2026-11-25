namespace UvA.Workflow.Api.WorkflowAuthoring;

public sealed class WorkflowAuthoringException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}