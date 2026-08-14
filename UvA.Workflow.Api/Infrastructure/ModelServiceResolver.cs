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

public record PendingBaselineInfo(string? ActiveCommit, string TargetCommit, DateTimeOffset LoadedAt);

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

    private record PendingEntry(
        ModelParser Parser,
        string DefaultMailLayout,
        string? ActiveCommit,
        string TargetCommit,
        DateTimeOffset LoadedAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly object _baselineGate = new();
    private PendingEntry? _pendingBaseline;

    public void AddOrUpdate(string version, ModelParser parser, string defaultMailLayout, string? commit = null,
        VersionKind kind = VersionKind.Upload)
    {
        _entries[version] = new Entry(parser, defaultMailLayout, commit, DateTimeOffset.UtcNow, kind);
        if (version == "")
            Volatile.Write(ref _pendingBaseline, null);
    }

    public void StagePendingBaseline(ModelParser parser, string defaultMailLayout, string targetCommit)
    {
        lock (_baselineGate)
        {
            var activeCommit = _entries.GetValueOrDefault("")?.Commit;
            Volatile.Write(ref _pendingBaseline,
                new PendingEntry(parser, defaultMailLayout, activeCommit, targetCommit, DateTimeOffset.UtcNow));
        }
    }

    public bool Contains(string version) => _entries.ContainsKey(version);

    public ResolvedWorkflowConfig Resolve()
    {
        var version = httpContextAccessor.HttpContext?.Request.Headers[VersionHeader].FirstOrDefault() ?? "";
        var entry = _entries.GetValueOrDefault(version) ?? _entries[""];
        return new ResolvedWorkflowConfig(new ModelService(entry.Parser), entry.DefaultMailLayout);
    }

    public IReadOnlyCollection<VersionInfo> GetVersions()
        => _entries.Select(kv => new VersionInfo(kv.Key, kv.Value.Commit, kv.Value.LoadedAt, kv.Value.Kind)).ToArray();

    public PendingBaselineInfo? GetPendingBaseline()
    {
        var pending = Volatile.Read(ref _pendingBaseline);
        return pending == null
            ? null
            : new PendingBaselineInfo(pending.ActiveCommit, pending.TargetCommit, pending.LoadedAt);
    }

    internal ModelParser? GetBaselineParser()
        => _entries.GetValueOrDefault("")?.Parser;

    internal bool PromotePendingBaseline(string expectedTargetCommit)
    {
        lock (_baselineGate)
        {
            var pending = Volatile.Read(ref _pendingBaseline);
            if (pending == null || pending.TargetCommit != expectedTargetCommit)
                return false;

            _entries[""] = new Entry(
                pending.Parser,
                pending.DefaultMailLayout,
                pending.TargetCommit,
                DateTimeOffset.UtcNow,
                VersionKind.Baseline);
            Volatile.Write(ref _pendingBaseline, null);
            return true;
        }
    }
}