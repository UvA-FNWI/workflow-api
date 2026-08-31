using System.Collections.Concurrent;

namespace UvA.Workflow.Api.Infrastructure;

/// How a loaded version got there: the default baseline, a git branch preview, or an uploaded set of files.
public enum VersionKind
{
    Baseline,
    Branch,
    Upload
}

/// A loaded config version and where it came from.
public record VersionInfo(string Name, string? Commit, DateTimeOffset LoadedAt, VersionKind Kind);

public record ResolvedWorkflowConfig(ModelService ModelService, string DefaultMailLayout);

/// Loaded workflow models and mail layouts, keyed by version. A request selects both with the Workflow-Version
/// header, or gets the default version, which is stored under the empty-string key.
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
        var entry = ResolveEntry();
        return new ResolvedWorkflowConfig(new ModelService(entry.Parser), entry.DefaultMailLayout);
    }

    /// The raw files behind the resolved version: yaml from the content provider, plus the mail layout,
    /// which is held separately on the entry but is required by POST /Versions/{version}.
    public Dictionary<string, string> ResolveFiles()
    {
        var entry = ResolveEntry();
        var files = ConfigFileReader.ReadAll(entry.Parser.ContentProvider);
        files[WorkflowConfigLoader.LayoutPath] = entry.DefaultMailLayout;
        return files;
    }

    private Entry ResolveEntry()
    {
        var version = httpContextAccessor.HttpContext?.Request.Headers["Workflow-Version"].FirstOrDefault() ?? "";
        return _entries.GetValueOrDefault(version) ?? _entries[""];
    }

    public IReadOnlyCollection<VersionInfo> GetVersions()
        => _entries.Select(kv => new VersionInfo(kv.Key, kv.Value.Commit, kv.Value.LoadedAt, kv.Value.Kind)).ToArray();
}