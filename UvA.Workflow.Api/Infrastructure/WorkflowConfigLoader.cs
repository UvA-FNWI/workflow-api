using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using UvA.Workflow.Notifications;

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
    MailTemplateStore mailTemplateStore,
    ILogger<WorkflowConfigLoader> logger)
{
    private readonly WorkflowSourceOptions _opts = options.Value;
    private readonly SemaphoreSlim _baselineLock = new(1, 1); // one baseline reload at a time (poller vs. /reload)

    // Last commit SHA seen per version (GitHub returns it as the ETag), replayed as If-None-Match so an unchanged
    // ref answers a free 304 instead of us re-resolving and re-downloading. Baseline is keyed by "".
    private readonly ConcurrentDictionary<string, EntityTagHeaderValue> _etags = new();

    // Conditional polling needs the last baseline SHA to send as If-None-Match; without it every poll re-downloads.
    public bool CanPoll => _etags.ContainsKey("");

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

    // Fetch @ref, install it under versionKey, and remember its ETag. Returns false when the ref is unchanged (304).
    private async Task<bool> LoadAsync(string @ref, string versionKey, VersionKind kind)
    {
        _etags.TryGetValue(versionKey, out var previousEtag);
        var source = await BuildProviderAsync(@ref, previousEtag);
        if (source is null)
            return false;

        var (provider, projectsDir, tempDir, etag) = source.Value;
        try
        {
            var revision = Revision(etag);
            resolver.AddOrUpdate(versionKey, new ModelParser(provider), revision, kind);
            // Only the default version carries the mail layout (Layouts is the sibling of Projects).
            if (kind == VersionKind.Baseline)
                mailTemplateStore.Default = ReadLayout(projectsDir);
            if (etag is not null)
                _etags[versionKey] = etag;
            else
                _etags.TryRemove(versionKey, out _);
            logger.LogInformation("Installed {Kind} config ref {Ref} at {Revision} (was {Previous})",
                kind, @ref, Short(revision), Short(Revision(previousEtag)));
            return true;
        }
        finally
        {
            DeleteTempDir(tempDir);
        }
    }

    private static string? ReadLayout(string projectsDir)
    {
        var layoutPath = Path.Combine(projectsDir, "..", "Layouts", "default.html");
        return File.Exists(layoutPath) ? File.ReadAllText(layoutPath) : null;
    }

    /// Conditionally re-fetch the baseline. Returns true when a new archive was installed.
    public Task<bool> ReloadBaselineIfChangedAsync()
        => string.IsNullOrWhiteSpace(_opts.RepoUrl) ? Task.FromResult(false) : LoadBaselineAsync();

    // Returns null for HTTP 304. The caller owns TempDir when a source is returned.
    private async Task<(IContentProvider Provider, string ProjectsDir, string? TempDir, EntityTagHeaderValue? ETag)?>
        BuildProviderAsync(string @ref, EntityTagHeaderValue? etag)
    {
        if (!string.IsNullOrWhiteSpace(_opts.LocalPath))
            return (new FileSystemProvider(_opts.LocalPath), _opts.LocalPath, null, null);

        if (string.IsNullOrWhiteSpace(_opts.RepoUrl))
            throw new InvalidOperationException(
                "No workflow source configured; set WorkflowSource:RepoUrl or WorkflowSource:LocalPath");

        var archive = await FetchAndExtractAsync(@ref, etag);
        if (archive is null)
            return null;
        var (root, tempDir, archiveEtag) = archive.Value;
        var projectsDir = Path.Combine(root, "Projects");
        return (new FileSystemProvider(projectsDir), projectsDir, tempDir, archiveEtag);
    }

    private async Task<(string Root, string TempDir, EntityTagHeaderValue? ETag)?> FetchAndExtractAsync(
        string @ref, EntityTagHeaderValue? etag)
    {
        var (owner, repo) = ParseRepo(_opts.RepoUrl!);
        var http = httpClientFactory.CreateClient(nameof(WorkflowConfigLoader));

        // Resolve ref -> commit SHA conditionally. GitHub returns the SHA as the ETag and answers 304 (which
        // doesn't count against the rate limit) when the ref hasn't moved, so idle polls cost nothing. We only
        // download the tarball below when the ref actually changed.
        using var shaRequest = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{owner}/{repo}/commits/{@ref}");
        shaRequest.Headers.Accept.ParseAdd("application/vnd.github.sha");
        if (etag is not null)
            shaRequest.Headers.IfNoneMatch.Add(etag);

        using var shaResponse = await http.SendAsync(shaRequest);
        if (shaResponse.StatusCode == HttpStatusCode.NotModified && etag is not null)
            return null;
        if (!shaResponse.IsSuccessStatusCode)
            throw new WorkflowConfigFetchException(shaResponse.StatusCode, RetryAfter(shaResponse.Headers.RetryAfter));
        var sha = (await shaResponse.Content.ReadAsStringAsync()).Trim();

        // Download the resolved commit, not the ref: immutable, so no race with a branch that moves mid-fetch.
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://codeload.github.com/{owner}/{repo}/tar.gz/{sha}");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new WorkflowConfigFetchException(response.StatusCode, RetryAfter(response.Headers.RetryAfter));

        var tempDir = Path.Combine(Path.GetTempPath(), "milestones-config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await using (var stream = await response.Content.ReadAsStreamAsync())
            await using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                await TarFile.ExtractToDirectoryAsync(gzip, tempDir, overwriteFiles: true);

            // codeload wraps everything in a single top-level folder.
            var root = Directory.GetDirectories(tempDir).Single();
            return (root, tempDir, shaResponse.Headers.ETag); // ETag is the commit SHA (see Revision)
        }
        catch
        {
            DeleteTempDir(tempDir);
            throw;
        }
    }

    private void DeleteTempDir(string? tempDir)
    {
        if (tempDir is null) return;
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temp config dir {Dir}", tempDir);
        }
    }

    // GitHub's commits endpoint returns the SHA as its ETag, so this is the real commit hash.
    private static string? Revision(EntityTagHeaderValue? etag) => etag?.Tag.Trim('"');

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

    private static (string Owner, string Repo) ParseRepo(string repoUrl)
    {
        var parts = new Uri(repoUrl).AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2)
            throw new InvalidOperationException($"Invalid WorkflowSource:RepoUrl '{repoUrl}'");
        return (parts[0], parts[1].Replace(".git", ""));
    }
}