using System.Collections.Concurrent;

namespace UvA.Workflow.Api.Infrastructure;

/// A loaded config version and where it came from.
public record VersionInfo(string Name, string? Commit, DateTimeOffset LoadedAt);

/// Loaded workflow models, keyed by version. A request selects one with the Workflow-Version header, or
/// gets the default version, which is stored under the empty-string key.
public class ModelServiceResolver(IHttpContextAccessor httpContextAccessor)
{
    private record Entry(ModelParser Parser, string? Commit, DateTimeOffset LoadedAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public void AddOrUpdate(string version, ModelParser parser, string? commit = null)
        => _entries[version] = new Entry(parser, commit, DateTimeOffset.UtcNow);

    public ModelService Get()
    {
        var version = httpContextAccessor.HttpContext?.Request.Headers["Workflow-Version"].FirstOrDefault() ?? "";
        var entry = _entries.GetValueOrDefault(version) ?? _entries[""];
        return new ModelService(entry.Parser);
    }

    public IReadOnlyCollection<VersionInfo> GetVersions()
        => _entries.Select(kv => new VersionInfo(kv.Key, kv.Value.Commit, kv.Value.LoadedAt)).ToArray();
}