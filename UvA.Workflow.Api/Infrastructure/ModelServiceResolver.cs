namespace UvA.Workflow.Api.Infrastructure;

/// <summary>
/// Dynamically gets the workflow model based on the Workflow-Version http header. 
/// </summary>
public class ModelServiceResolver(IHttpContextAccessor httpContextAccessor)
{
    private readonly Dictionary<string, ModelParser> _parsers = new();

    public IEnumerable<string> Versions => _parsers.Keys;

    public void AddOrUpdate(string version, ModelParser parser)
        => _parsers[version] = parser;

    public ModelService Get()
    {
        var version = httpContextAccessor.HttpContext?.Request.Headers["Workflow-Version"].FirstOrDefault() ?? "";
        return new ModelService(_parsers.GetValueOrDefault(version) ?? _parsers[""]);
    }
}