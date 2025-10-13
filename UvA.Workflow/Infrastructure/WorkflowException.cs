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
    
    protected WorkflowException(System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context)
        : base(info, context)
    {
        Code = info.GetString(nameof(Code)) ?? string.Empty;
        Details = info.GetValue(nameof(Details), typeof(object));
    }
}