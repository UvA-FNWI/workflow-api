using System.Formats.Tar;
using System.IO.Compression;
using UvA.Workflow.Notifications;

namespace UvA.Workflow.Api.Infrastructure;

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

    /// Boot load: never throws, so a bad config repo can't crash-loop the pod; falls back to the bundled definitions.
    public async Task LoadBaselineOrFallbackAsync()
    {
        try
        {
            await LoadBaselineAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Baseline config load failed at startup; falling back to baked config");
            var projectsDir = BakedProjectsDir();
            InstallBaseline(new FileSystemProvider(projectsDir), null, projectsDir);
        }
    }

    /// Fetch the ref and install it as the default version. Throws on failure, so a failed reload keeps the current one.
    public async Task LoadBaselineAsync()
    {
        var (provider, commit, projectsDir) = await BuildProviderAsync(_opts.Ref);
        InstallBaseline(provider, commit, projectsDir);
        logger.LogInformation("Loaded baseline config ref {Ref} commit {Commit}", _opts.Ref, commit ?? "(local)");
    }

    // The default version and its mail layout come from the same source (Layouts is the sibling of Projects).
    private void InstallBaseline(IContentProvider provider, string? commit, string projectsDir)
    {
        resolver.AddOrUpdate("", new ModelParser(provider), commit);
        var layoutPath = Path.Combine(projectsDir, "..", "Layouts", "default.html");
        mailTemplateStore.Default = File.Exists(layoutPath) ? File.ReadAllText(layoutPath) : null;
    }

    /// Load a branch as a named preview version, keyed by the ref.
    public async Task LoadBranchAsync(string @ref)
    {
        if (string.IsNullOrWhiteSpace(_opts.RepoUrl))
            throw new InvalidOperationException("Loading a branch requires WorkflowSource:RepoUrl to be configured");
        ValidateRef(@ref);
        var (provider, commit, _) = await BuildProviderAsync(@ref);
        resolver.AddOrUpdate(@ref, new ModelParser(provider), commit);
        logger.LogInformation("Loaded preview config ref {Ref} commit {Commit}", @ref, commit);
    }

    // Failures propagate; only the boot path catches them. Returns the Projects dir so the caller can
    // read the sibling Layouts/ from the same source.
    private async Task<(IContentProvider Provider, string? Commit, string ProjectsDir)> BuildProviderAsync(string @ref)
    {
        if (!string.IsNullOrWhiteSpace(_opts.LocalPath))
            return (new FileSystemProvider(_opts.LocalPath), null, _opts.LocalPath);

        // No source configured: the definitions bundled with the app.
        if (string.IsNullOrWhiteSpace(_opts.RepoUrl))
        {
            var baked = BakedProjectsDir();
            return (new FileSystemProvider(baked), null, baked);
        }

        var (root, commit) = await FetchAndExtractAsync(@ref);
        var projectsDir = Path.Combine(root, "Projects");
        return (new FileSystemProvider(projectsDir), commit, projectsDir);
    }

    private async Task<(string Root, string? Commit)> FetchAndExtractAsync(string @ref)
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
        await using (var stream = await resp.Content.ReadAsStreamAsync())
        await using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
            await TarFile.ExtractToDirectoryAsync(gzip, tempDir, overwriteFiles: true);

        // codeload wraps everything in a single top-level "{repo}-{ref}" folder.
        var root = Directory.GetDirectories(tempDir).Single();
        return (root, commit);
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

    private static string BakedProjectsDir()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../Examples/Projects");

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