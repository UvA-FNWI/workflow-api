using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Notifications;
using UvA.Workflow.Tests.Helpers;

namespace UvA.Workflow.Tests;

public class WorkflowConfigLoaderTests
{
    private static readonly string FixturesPath = UnitTestsHelpers.FixturesProjectsPath;

    private static ModelServiceResolver CreateResolver()
        => new(new Mock<IHttpContextAccessor>().Object);

    private static WorkflowConfigLoader CreateLoader(ModelServiceResolver resolver, WorkflowSourceOptions opts,
        HttpMessageHandler? handler = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        if (handler is not null)
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        return new WorkflowConfigLoader(factory.Object, resolver, Options.Create(opts),
            new MailTemplateStore(), NullLogger<WorkflowConfigLoader>.Instance);
    }

    private static WorkflowSourceOptions RepoOptions()
        => new() { RepoUrl = "https://github.com/owner/repo", Ref = "main" };

    [Fact]
    public async Task LoadBaseline_FromLocalPath_RegistersBaselineWithDefinitions()
    {
        var resolver = CreateResolver();

        await CreateLoader(resolver, new WorkflowSourceOptions { LocalPath = FixturesPath }).LoadBaselineAsync();

        Assert.True(resolver.Get().WorkflowDefinitions.ContainsKey("Project"));
        Assert.Contains(resolver.GetVersions(), v => v.Name == "");
    }

    [Fact]
    public async Task LoadBaseline_NoSourceConfigured_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateLoader(CreateResolver(), new WorkflowSourceOptions()).LoadBaselineAsync());
    }

    [Fact]
    public async Task LoadBranch_WithoutRepoUrl_Throws()
    {
        var loader = CreateLoader(CreateResolver(), new WorkflowSourceOptions { LocalPath = FixturesPath });

        await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadBranchAsync("feature/x"));
    }

    [Fact]
    public async Task LoadBranch_InvalidRef_Throws()
    {
        var loader = CreateLoader(CreateResolver(), RepoOptions());

        await Assert.ThrowsAsync<ArgumentException>(() => loader.LoadBranchAsync("x/../../other/repo"));
    }

    [Fact]
    public async Task Startup_ResolvesRefThenDownloadsResolvedCommit()
    {
        var requests = new List<(Uri? Uri, string? IfNoneMatch)>();
        var github = new FakeGitHub("sha-1",
            r => requests.Add((r.RequestUri, r.Headers.IfNoneMatch.SingleOrDefault()?.ToString())));
        var resolver = CreateResolver();
        var loader = CreateLoader(resolver, RepoOptions(), github.Handler());

        Assert.True(await loader.LoadBaselineAsync());

        var version = Assert.Single(resolver.GetVersions());
        Assert.Equal("sha-1", version.Commit);
        Assert.True(loader.CanPoll);
        // First an unconditional ref -> SHA resolve on the API, then the resolved commit from codeload.
        Assert.Equal("api.github.com", requests[0].Uri?.Host);
        Assert.Null(requests[0].IfNoneMatch);
        Assert.Equal("codeload.github.com", requests[1].Uri?.Host);
        Assert.EndsWith("/sha-1", requests[1].Uri?.AbsolutePath);
    }

    [Fact]
    public async Task UnchangedBaseline_SendsEtagAndPreservesLoadedAt()
    {
        string? conditional = null;
        var github = new FakeGitHub("sha-1",
            r =>
            {
                if (r.RequestUri!.Host == "api.github.com")
                    conditional = r.Headers.IfNoneMatch.SingleOrDefault()?.ToString();
            });
        var resolver = CreateResolver();
        var loader = CreateLoader(resolver, RepoOptions(), github.Handler());
        await loader.LoadBaselineAsync();
        var loadedAt = Assert.Single(resolver.GetVersions()).LoadedAt;

        Assert.False(await loader.ReloadBaselineIfChangedAsync());

        Assert.Equal("\"sha-1\"", conditional);
        Assert.Equal(loadedAt, Assert.Single(resolver.GetVersions()).LoadedAt);
    }

    [Fact]
    public async Task ChangedBaseline_InstallsArchiveAndUpdatesEtag()
    {
        var github = new FakeGitHub("sha-1");
        var resolver = CreateResolver();
        var loader = CreateLoader(resolver, RepoOptions(), github.Handler());
        await loader.LoadBaselineAsync();

        github.Sha = "sha-2";
        Assert.True(await loader.ReloadBaselineIfChangedAsync());

        Assert.Equal("sha-2", Assert.Single(resolver.GetVersions()).Commit);
    }

    [Fact]
    public async Task InvalidChangedArchive_PreservesPreviousModelAndEtag()
    {
        var conditionals = new List<string?>();
        var github = new FakeGitHub("sha-1",
            r =>
            {
                if (r.RequestUri!.Host == "api.github.com")
                    conditionals.Add(r.Headers.IfNoneMatch.SingleOrDefault()?.ToString());
            });
        var resolver = CreateResolver();
        var loader = CreateLoader(resolver, RepoOptions(), github.Handler());
        await loader.LoadBaselineAsync();
        var previous = Assert.Single(resolver.GetVersions());

        github.Sha = "sha-2";
        github.Archive = CreateInvalidArchive;
        await Assert.ThrowsAnyAsync<Exception>(() => loader.ReloadBaselineIfChangedAsync());
        var retained = Assert.Single(resolver.GetVersions());
        await Assert.ThrowsAnyAsync<Exception>(() => loader.ReloadBaselineIfChangedAsync());

        Assert.Equal(previous, retained);
        // The bad install never advanced the stored SHA, so we keep re-resolving against the last good one.
        Assert.Equal("\"sha-1\"", conditionals[^1]);
    }

    [Theory]
    [InlineData("feature/x")]
    [InlineData("v1.2.3")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public async Task LoadRef_ResolvesRefThenDownloadsResolvedCommit(string @ref)
    {
        var requests = new List<Uri?>();
        var github = new FakeGitHub("resolved-sha", r => requests.Add(r.RequestUri));
        var resolver = CreateResolver();

        await CreateLoader(resolver, RepoOptions(), github.Handler()).LoadBranchAsync(@ref);

        // The ref is resolved on the API (ref stays in the path)...
        Assert.Contains(requests, u => u!.Host == "api.github.com" && u.AbsolutePath.EndsWith($"/commits/{@ref}"));
        // ...then the resolved commit, not the ref, is what we download.
        Assert.Contains(requests, u => u!.Host == "codeload.github.com" && u.AbsolutePath.EndsWith("/resolved-sha"));
        Assert.Contains(resolver.GetVersions(), v => v.Name == @ref && v.Commit == "resolved-sha");
    }

    [Fact]
    public async Task UnchangedPreview_SendsEtagAndPreservesLoadedAt()
    {
        string? conditional = null;
        var github = new FakeGitHub("sha-1",
            r =>
            {
                if (r.RequestUri!.Host == "api.github.com")
                    conditional = r.Headers.IfNoneMatch.SingleOrDefault()?.ToString();
            });
        var resolver = CreateResolver();
        var loader = CreateLoader(resolver, RepoOptions(), github.Handler());
        await loader.LoadBranchAsync("feature/x");
        var loadedAt = Assert.Single(resolver.GetVersions()).LoadedAt;

        await loader.LoadBranchAsync("feature/x");

        Assert.Equal("\"sha-1\"", conditional);
        Assert.Equal(loadedAt, Assert.Single(resolver.GetVersions()).LoadedAt);
    }

    [Fact]
    public async Task ManualAndBackgroundBaselineReloads_AreSerialized()
    {
        var active = 0;
        var maxActive = 0;
        var gate = new object();
        // First call resolves sha-1; the concurrent reloads hit the conditional resolve, which we hold open
        // long enough to detect any overlap.
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            if (request.RequestUri!.Host == "codeload.github.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CreateArchive()) };

            if (request.Headers.IfNoneMatch.SingleOrDefault()?.Tag.Trim('"') == "sha-1")
            {
                lock (gate)
                    maxActive = Math.Max(maxActive, ++active);
                await Task.Delay(50);
                lock (gate)
                    active--;
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var resolved = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("sha-1") };
            resolved.Headers.ETag = new EntityTagHeaderValue("\"sha-1\"");
            return resolved;
        });
        var loader = CreateLoader(CreateResolver(), RepoOptions(), handler);
        await loader.LoadBaselineAsync();

        await Task.WhenAll(loader.LoadBaselineAsync(), loader.ReloadBaselineIfChangedAsync());

        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task StartupWithoutEtag_DisablesPolling()
    {
        // Resolve succeeds but carries no ETag, so there is nothing to send as If-None-Match later.
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host == "api.github.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("sha-1") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CreateArchive()) }));
        var loader = CreateLoader(CreateResolver(), RepoOptions(), handler);

        await loader.LoadBaselineAsync();

        Assert.False(loader.CanPoll);
    }

    [Fact]
    public async Task FetchFailure_ExposesRetryAfter()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            return Task.FromResult(response);
        });
        var loader = CreateLoader(CreateResolver(), RepoOptions(), handler);

        var exception = await Assert.ThrowsAsync<WorkflowConfigFetchException>(() => loader.LoadBaselineAsync());

        Assert.Equal(TimeSpan.FromSeconds(17), exception.RetryAfter);
    }

    [Fact]
    public async Task ReloadBaselineIfChanged_NoRepoUrl_ReturnsFalse()
    {
        var loader = CreateLoader(CreateResolver(), new WorkflowSourceOptions { LocalPath = FixturesPath });

        Assert.False(await loader.ReloadBaselineIfChangedAsync());
    }

    // Stands in for GitHub: api.github.com resolves the ref to Sha (returned as the ETag, 304 when the caller
    // already holds it); codeload.github.com serves the current Archive for the resolved commit.
    private sealed class FakeGitHub(string sha, Action<HttpRequestMessage>? observe = null)
    {
        public string Sha { get; set; } = sha;
        public Func<byte[]> Archive { get; set; } = CreateArchive;

        public HttpMessageHandler Handler() => new StubHttpMessageHandler((request, _) =>
        {
            observe?.Invoke(request);
            if (request.RequestUri!.Host == "api.github.com")
            {
                if (request.Headers.IfNoneMatch.SingleOrDefault()?.Tag.Trim('"') == Sha)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
                var resolved = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sha) };
                resolved.Headers.ETag = new EntityTagHeaderValue($"\"{Sha}\"");
                return Task.FromResult(resolved);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive()) });
        });
    }

    private static byte[] CreateArchive()
    {
        var fixturesRoot = Directory.GetParent(FixturesPath)!.FullName;
        using var result = new MemoryStream();
        using (var gzip = new GZipStream(result, CompressionMode.Compress, leaveOpen: true))
            TarFile.CreateFromDirectory(fixturesRoot, gzip, includeBaseDirectory: true);
        return result.ToArray();
    }

    private static byte[] CreateInvalidArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "archive", "Projects", "Invalid");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Entity.yaml"), "invalid: [");
        try
        {
            using var result = new MemoryStream();
            using (var gzip = new GZipStream(result, CompressionMode.Compress, leaveOpen: true))
                TarFile.CreateFromDirectory(Path.Combine(root, "archive"), gzip, includeBaseDirectory: true);
            return result.ToArray();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request, cancellationToken);
    }
}