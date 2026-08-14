using System.Collections.Concurrent;

namespace UvA.Workflow.Api.Infrastructure;

public enum VersionKind
{
    Baseline,
    Branch,
    Upload
}

public record VersionInfo(string Name, string? Commit, DateTimeOffset LoadedAt, VersionKind Kind);

public record ResolvedWorkflowConfig(ModelService ModelService, string DefaultMailLayout);

public class ModelServiceResolver(IHttpContextAccessor httpContextAccessor)
{
    public const string VersionHeader = "Workflow-Version";

    private record Entry(
        ModelParser Parser,
        string DefaultMailLayout,
        string? Commit,
        DateTimeOffset LoadedAt,
        VersionKind Kind);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public void AddOrUpdate(string version, ModelParser parser, string defaultMailLayout, string? commit = null,
        VersionKind kind = VersionKind.Upload)
        => _entries[version] = new Entry(parser, defaultMailLayout, commit, DateTimeOffset.UtcNow, kind);

    public bool Contains(string version) => _entries.ContainsKey(version);

    public ResolvedWorkflowConfig Resolve()
    {
        var version = httpContextAccessor.HttpContext?.Request.Headers[VersionHeader].FirstOrDefault() ?? "";
        var entry = _entries.GetValueOrDefault(version) ?? _entries[""];
        return new ResolvedWorkflowConfig(new ModelService(entry.Parser), entry.DefaultMailLayout);
    }

    public IReadOnlyCollection<VersionInfo> GetVersions()
        => _entries.Select(kv => new VersionInfo(kv.Key, kv.Value.Commit, kv.Value.LoadedAt, kv.Value.Kind)).ToArray();
}