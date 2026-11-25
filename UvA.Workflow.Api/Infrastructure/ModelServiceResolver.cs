using System.Collections.Concurrent;
using UvA.Workflow.Api.WorkflowAuthoring;

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
    private static readonly object DraftPreviewKey = new();

    internal static void UseDraftPreview(HttpContext context, ModelParser parser)
        => context.Items[DraftPreviewKey] = parser;

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
        var context = httpContextAccessor.HttpContext;
        var isDraftPreview = context?.Request.Headers[VersionHeader].FirstOrDefault()
            ?.StartsWith(WorkflowPreviewSnapshot.Prefix, StringComparison.Ordinal) == true;
        // Authentication can resolve ModelService before the filter authorizes the preview.
        var model = isDraftPreview
            ? new ModelService(() => GetDraftPreview(context) ?? entry.Parser)
            : new ModelService(entry.Parser);
        return new ResolvedWorkflowConfig(model, entry.DefaultMailLayout);
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
        var context = httpContextAccessor.HttpContext;
        if (GetDraftPreview(context) is { } preview)
            return _entries[""] with { Parser = preview };

        var version = context?.Request.Headers[VersionHeader].FirstOrDefault() ?? "";
        return _entries.GetValueOrDefault(version) ?? _entries[""];
    }

    private static ModelParser? GetDraftPreview(HttpContext? context)
        => context?.Items.TryGetValue(DraftPreviewKey, out var value) == true ? value as ModelParser : null;

    public IReadOnlyCollection<VersionInfo> GetVersions()
        => _entries.Select(kv => new VersionInfo(kv.Key, kv.Value.Commit, kv.Value.LoadedAt, kv.Value.Kind)).ToArray();
}