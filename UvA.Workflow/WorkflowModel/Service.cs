namespace UvA.Workflow.WorkflowModel;

public class Service : INamed
{
    public string Name { get; set; } = null!;
    public string? BaseUrl { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();

    public List<ServiceOperation> Operations { get; set; } = [];
}

public class ServiceOperation : INamed
{
    public string Name { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string Method { get; set; } = "GET";

    public List<ServiceInput> Inputs { get; set; } = [];
    public List<ServiceOutput> Outputs { get; set; } = [];

    public object? Body { get; set; }
}

public class ServiceInput
{
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
}

public class ServiceOutput
{
    public string Name { get; set; } = null!;
    public string Path { get; set; } = null!;
}