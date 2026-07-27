using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace UvA.Workflow.Api.Infrastructure;

public sealed class WorkflowConfigFetchException(
    HttpStatusCode statusCode,
    TimeSpan? retryAfter)
    : HttpRequestException($"Config download failed with status {(int)statusCode} ({statusCode})", null, statusCode)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// Loads workflow definitions (from a local dir, or a GitHub repo fetched as a tarball) into
/// the <see cref="ModelServiceResolver"/>.
public class WorkflowConfigLoader(
    IHttpClientFactory httpClientFactory,
    ModelServiceResolver resolver,
    IOptions<WorkflowSourceOptions> options,
    ILogger<WorkflowConfigLoader> logger)
{
    // Repo-root-relative path to the default mail layout; read alongside, but outside, the workflow model.
    public const string LayoutPath = "Layouts/default.html";

    private readonly WorkflowSourceOptions _opts = options.Value;
    private readonly SemaphoreSlim _baselineLock = new(1, 1); // one baseline reload at a time (poller vs. /reload)

    // Last commit SHA installed per version; we compare the freshly-resolved SHA against it to skip the tarball
    // download + re-parse when the ref hasn't moved. Baseline is keyed by "".
    private readonly ConcurrentDictionary<string, string> _shas = new();

    // Polling needs a baseline SHA to diff against; LocalPath dev mode has none, so it won't poll.
    public bool CanPoll => _shas.ContainsKey("");

    /// Fetch the configured ref and install it as the default version. Returns false when it has not changed.
    public async Task<bool> LoadBaselineAsync()
    {
        await _baselineLock.WaitAsync();
        try
        {
            return await LoadAsync(_opts.Ref, "", VersionKind.Baseline);
        }
        finally
        {
            _baselineLock.Release();
        }
    }

    /// Load a branch, tag, or SHA as a named preview version, keyed by the ref.
    public async Task LoadBranchAsync(string @ref)
    {
        if (string.IsNullOrWhiteSpace(_opts.RepoUrl))
            throw new InvalidOperationException("Loading a ref requires WorkflowSource:RepoUrl to be configured");
        ValidateRef(@ref);
        await LoadAsync(@ref, @ref, VersionKind.Branch);
    }

    // Fetch @ref, install it under versionKey, and remember its SHA. Returns false when the ref is unchanged.
    private async Task<bool> LoadAsync(string @ref, string versionKey, VersionKind kind)
    {
        _shas.TryGetValue(versionKey, out var previousSha);
        var model = await BuildModelAsync(@ref, previousSha);
        if (model is null)
            return false;

        var (parser, layout, sha) = model.Value;
        resolver.AddOrUpdate(versionKey, parser, layout, sha, kind);
        if (sha is not null)
            _shas[versionKey] = sha;
        else
            _shas.TryRemove(versionKey, out _);
        logger.LogInformation("Installed {Kind} config ref {Ref} at {Revision} (was {Previous})",
            kind, @ref, Short(sha), Short(previousSha));
        return true;
    }

    /// Re-fetch the baseline; returns true when a newer commit was installed.
    public Task<bool> ReloadBaselineIfChangedAsync()
        => string.IsNullOrWhiteSpace(_opts.RepoUrl) ? Task.FromResult(false) : LoadBaselineAsync();

    // Build the model + layout for @ref. Returns null when the ref hasn't moved off previousSha.
    private async Task<(ModelParser Parser, string Layout, string? Sha)?> BuildModelAsync(
        string @ref, string? previousSha)
    {
        if (!string.IsNullOrWhiteSpace(_opts.LocalPath))
        {
            logger.LogDebug("Loading local path {LocalPath}", _opts.LocalPath);
            var layoutPath = Path.Combine(_opts.LocalPath, LayoutPath);
            if (!File.Exists(layoutPath))
                throw new FileNotFoundException($"Default mail layout not found: {layoutPath}", layoutPath);
            return (new ModelParser(new FileSystemProvider(_opts.LocalPath)), await File.ReadAllTextAsync(layoutPath),
                null);
        }

        if (string.IsNullOrWhiteSpace(_opts.RepoUrl))
            throw new InvalidOperationException(
                "No workflow source configured; set WorkflowSource:RepoUrl or WorkflowSource:LocalPath");

        var fetched = await FetchAsync(@ref, previousSha);
        if (fetched is null)
            return null;

        var (files, sha) = fetched.Value;
        // ReadTarballAsync whitelists to yaml + the layout, so pulling the layout out leaves exactly the yaml.
        if (!files.Remove(LayoutPath, out var layout) || string.IsNullOrWhiteSpace(layout))
            throw new FileNotFoundException($"Default mail layout not found: {LayoutPath}", LayoutPath);
        return (new ModelParser(new DictionaryProvider(files)), layout, sha);
    }

    // Resolve @ref to a commit and pull its yaml + layout files into memory, keyed by their repo-root-relative
    // path. Returns null when the ref still resolves to previousSha.
    private async Task<(Dictionary<string, string> Files, string Sha)?> FetchAsync(string @ref, string? previousSha)
    {
        var (owner, repo) = ParseRepo(_opts.RepoUrl!);
        var http = httpClientFactory.CreateClient(nameof(WorkflowConfigLoader));

        // Resolve ref -> commit SHA. This is the one request that counts against the rate limit (codeload
        // downloads don't). We compare the SHA to the one we last installed, and skip the tarball download +
        // re-parse when the ref hasn't moved.
        using var shaRequest = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{owner}/{repo}/commits/{EncodeRefPath(@ref)}");
        shaRequest.Headers.Accept.ParseAdd("application/vnd.github.sha");
        using var shaResponse = await http.SendAsync(shaRequest);
        if (!shaResponse.IsSuccessStatusCode)
            throw new WorkflowConfigFetchException(shaResponse.StatusCode, RetryAfter(shaResponse.Headers.RetryAfter));
        var sha = (await shaResponse.Content.ReadAsStringAsync()).Trim();
        if (sha == previousSha)
            return null;

        // Download the resolved commit, not the ref: immutable, so no race with a branch that moves mid-fetch.
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://codeload.github.com/{owner}/{repo}/tar.gz/{sha}");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new WorkflowConfigFetchException(response.StatusCode, RetryAfter(response.Headers.RetryAfter));

        await using var stream = await response.Content.ReadAsStreamAsync();
        return (await ReadTarballAsync(stream), sha);
    }

    // Read the yaml files and the mail layout out of a codeload tarball into memory, keyed by their path
    // relative to the single top-level folder codeload wraps everything in.
    private static async Task<Dictionary<string, string>> ReadTarballAsync(Stream tarGz)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var gzip = new GZipStream(tarGz, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            if (entry.DataStream is null)
                continue;
            var slash = entry.Name.IndexOf('/');
            if (slash < 0)
                continue;
            var path = entry.Name[(slash + 1)..];
            if (!path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) && path != LayoutPath)
                continue;
            using var text = new StreamReader(entry.DataStream);
            files[path] = await text.ReadToEndAsync();
        }

        return files;
    }

    // Short git-style revision for log lines; the full SHA is still what we store on the version.
    private static string Short(string? revision)
        => string.IsNullOrEmpty(revision) ? "unknown" : revision[..Math.Min(7, revision.Length)];

    // Retry-After is either a delay in seconds (Delta) or an HTTP date; normalize both, clamped at zero if past.
    private static TimeSpan? RetryAfter(RetryConditionHeaderValue? value)
        => value?.Delta ?? (value?.Date is { } date
            ? TimeSpan.FromTicks(Math.Max(0, (date - DateTimeOffset.UtcNow).Ticks))
            : null);

    // Git refs can't contain these; also blocks path traversal into another repo via the URL.
    private static void ValidateRef(string @ref)
    {
        if (@ref.Contains("..") || @ref.StartsWith('/') || @ref.EndsWith('/')
            || @ref.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException($"Invalid ref '{@ref}'");
    }

    // Percent-encode each segment so URL-reserved chars a git ref may contain (e.g. '#' in feature/#123) don't
    // corrupt the request, while keeping '/' literal.
    private static string EncodeRefPath(string @ref)
        => string.Join('/', @ref.Split('/').Select(Uri.EscapeDataString));

    private static (string Owner, string Repo) ParseRepo(string repoUrl)
    {
        var parts = new Uri(repoUrl).AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2)
            throw new InvalidOperationException($"Invalid WorkflowSource:RepoUrl '{repoUrl}'");
        return (parts[0], parts[1].Replace(".git", ""));
    }
}