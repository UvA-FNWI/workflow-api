using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using UvA.Workflow.Notifications;

namespace UvA.Workflow.Api.Infrastructure;

/// Loads workflow definitions (from a local dir, or a GitHub repo fetched as a tarball) into
/// the <see cref="ModelServiceResolver"/>.
public class WorkflowConfigLoader(
    IHttpClientFactory httpClientFactory,
    ModelServiceResolver resolver,
    IOptions<WorkflowSourceOptions> options,
    MailTemplateStore mailTemplateStore,
    IMemoryCache cache,
    ILogger<WorkflowConfigLoader> logger)
{
    private readonly WorkflowSourceOptions _opts = options.Value;

    /// Fetch the ref and install it as the default version. Throws on failure.
    public async Task LoadBaselineAsync()
    {
        var (provider, commit, projectsDir, tempDir) = await BuildProviderAsync(_opts.Ref);
        try
        {
            InstallBaseline(provider, commit, projectsDir);
            logger.LogInformation("Loaded baseline config ref {Ref} commit {Commit}", _opts.Ref, commit ?? "(local)");
        }
        finally
        {
            DeleteTempDir(tempDir);
        }
    }

    // The default version and its mail layout come from the same source (Layouts is the sibling of Projects).
    private void InstallBaseline(IContentProvider provider, string? commit, string projectsDir)
    {
        resolver.AddOrUpdate("", new ModelParser(provider), commit, VersionKind.Baseline);
        var layoutPath = Path.Combine(projectsDir, "..", "Layouts", "default.html");
        mailTemplateStore.Default = File.Exists(layoutPath) ? File.ReadAllText(layoutPath) : null;
    }

    /// Load a branch as a named preview version, keyed by the ref.
    public async Task LoadBranchAsync(string @ref)
    {
        if (string.IsNullOrWhiteSpace(_opts.RepoUrl))
            throw new InvalidOperationException("Loading a branch requires WorkflowSource:RepoUrl to be configured");
        ValidateRef(@ref);
        var (provider, commit, _, tempDir) = await BuildProviderAsync(@ref);
        try
        {
            resolver.AddOrUpdate(@ref, new ModelParser(provider), commit, VersionKind.Branch);
            logger.LogInformation("Loaded preview config ref {Ref} commit {Commit}", @ref, commit);
        }
        finally
        {
            DeleteTempDir(tempDir);
        }
    }

    // Returns the Projects dir so the caller can read the sibling Layouts/ from the same source, plus the temp
    // extraction dir to delete once parsing is done.
    private async Task<(IContentProvider Provider, string? Commit, string ProjectsDir, string? TempDir)>
        BuildProviderAsync(string @ref)
    {
        if (!string.IsNullOrWhiteSpace(_opts.LocalPath))
            return (new FileSystemProvider(_opts.LocalPath), null, _opts.LocalPath, null);

        if (string.IsNullOrWhiteSpace(_opts.RepoUrl))
            throw new InvalidOperationException(
                "No workflow source configured; set WorkflowSource:RepoUrl or WorkflowSource:LocalPath");

        var (root, commit, tempDir) = await FetchAndExtractAsync(@ref);
        var projectsDir = Path.Combine(root, "Projects");
        return (new FileSystemProvider(projectsDir), commit, projectsDir, tempDir);
    }

    private async Task<(string Root, string? Commit, string TempDir)> FetchAndExtractAsync(string @ref)
    {
        var (owner, repo) = ParseRepo(_opts.RepoUrl!);
        var http = httpClientFactory.CreateClient(nameof(WorkflowConfigLoader));

        // Resolve ref -> SHA first (provenance + an atomic fetch); best-effort.
        var commit = await ResolveShaAsync(http, owner, repo, @ref);
        var download = commit ?? @ref;

        using var resp = await http.GetAsync(
            $"https://codeload.github.com/{owner}/{repo}/tar.gz/{download}",
            HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var tempDir = Path.Combine(Path.GetTempPath(), "milestones-config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await using (var stream = await resp.Content.ReadAsStreamAsync())
            await using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                await TarFile.ExtractToDirectoryAsync(gzip, tempDir, overwriteFiles: true);

            // codeload wraps everything in a single top-level "{repo}-{ref}" folder.
            var root = Directory.GetDirectories(tempDir).Single();
            return (root, commit, tempDir);
        }
        catch
        {
            DeleteTempDir(tempDir); // don't leak the dir if extraction fails
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

    private static async Task<string?> ResolveShaAsync(HttpClient http, string owner, string repo, string @ref)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{owner}/{repo}/commits/{@ref}");
            req.Headers.Accept.ParseAdd("application/vnd.github.sha");
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var sha = (await resp.Content.ReadAsStringAsync()).Trim();
            return sha.Length == 40 ? sha : null;
        }
        catch
        {
            return null; // best-effort provenance; fall back to fetching by ref
        }
    }

    // Git refs can't contain these; also blocks path traversal into another repo via the URL.
    private static void ValidateRef(string @ref)
    {
        if (@ref.Contains("..") || @ref.StartsWith('/') || @ref.EndsWith('/')
            || @ref.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException($"Invalid ref '{@ref}'");
    }

    private sealed record GitHubBranch([property: JsonPropertyName("name")] string Name);

    /// List branch names of the configured repo (cached briefly). Empty when no RepoUrl (LocalPath dev mode).
    public async Task<IReadOnlyList<string>> ListBranchesAsync()
    {
        if (string.IsNullOrWhiteSpace(_opts.RepoUrl))
            return [];

        return await cache.GetOrCreateAsync("workflow:branches", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            var (owner, repo) = ParseRepo(_opts.RepoUrl!);
            var http = httpClientFactory.CreateClient(nameof(WorkflowConfigLoader));
            var branches = await http.GetFromJsonAsync<GitHubBranch[]>(
                $"https://api.github.com/repos/{owner}/{repo}/branches?per_page=100");
            return branches?.Select(b => b.Name).ToArray();
        }) ?? [];
    }

    private static (string Owner, string Repo) ParseRepo(string repoUrl)
    {
        var parts = new Uri(repoUrl).AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2)
            throw new InvalidOperationException($"Invalid WorkflowSource:RepoUrl '{repoUrl}'");
        return (parts[0], parts[1].Replace(".git", ""));
    }
}