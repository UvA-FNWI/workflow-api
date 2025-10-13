namespace UvA.Workflow.Infrastructure;

[Serializable]
public class WorkflowException: Exception
{
    public string Code { get; private set; }
    public object? Details { get; private set; }
    
    public WorkflowException(string code, string message, object? details = null)
    : base(message)
    {
        Code = code;
        Details = details;
    }
}