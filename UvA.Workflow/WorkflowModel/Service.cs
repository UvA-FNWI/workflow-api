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

    /// <summary>
    /// The name of a top-level property in the JSON response body to extract as a string.
    /// Mutually exclusive with <see cref="Template"/>; exactly one must be set.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// A <c>{{ }}</c> template string evaluated to produce the output value.
    /// Mutually exclusive with <see cref="Path"/>; exactly one must be set.
    ///
    /// The template is resolved against a merged context with the following priority
    /// (highest wins on key collision):
    ///   1. JSON response root properties  — top-level properties from the HTTP response body
    ///   2. Service config values          — keys from the service's config section (e.g. base URLs, tokens)
    ///   3. Main workflow context          — workflow instance properties and user data
    /// </summary>
    public string? Template { get; set; }
}