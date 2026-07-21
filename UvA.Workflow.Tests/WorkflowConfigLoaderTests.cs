using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Versions;
using UvA.Workflow.Notifications;
using UvA.Workflow.Persistence;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests;

public class WorkflowConfigLoaderTests
{
    private static readonly string FixturesPath = UnitTestsHelpers.FixturesProjectsPath;
    private static readonly string FixturesRoot = Directory.GetParent(FixturesPath)!.FullName;

    private static ModelServiceResolver CreateResolver(IHttpContextAccessor? accessor = null)
        => new(accessor ?? new Mock<IHttpContextAccessor>().Object);

    private static WorkflowConfigLoader CreateLoader(ModelServiceResolver resolver, WorkflowSourceOptions opts,
        HttpMessageHandler? handler = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        if (handler is not null)
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        return new WorkflowConfigLoader(factory.Object, resolver, Options.Create(opts),
            NullLogger<WorkflowConfigLoader>.Instance);
    }

    private static WorkflowSourceOptions RepoOptions()
        => new() { RepoUrl = "https://github.com/owner/repo", Ref = "main" };

    [Fact]
    public async Task LoadBaseline_FromLocalCheckout_LoadsProjectsAndLayout()
    {
        var resolver = CreateResolver();

        await CreateLoader(resolver, new WorkflowSourceOptions { LocalPath = FixturesRoot }).LoadBaselineAsync();

        var config = resolver.Resolve();
        Assert.True(config.ModelService.WorkflowDefinitions.ContainsKey("Project"));
        Assert.Contains(resolver.GetVersions(), v => v.Name == "");
        Assert.Equal(File.ReadAllText(Path.Combine(FixturesRoot, "Layouts", "default.html")),
            config.DefaultMailLayout);
    }

    [Fact]
    public void Resolve_SelectsLayoutByWorkflowVersionAndFallsBackToBaseline()
    {
        var context = new DefaultHttpContext();
        var resolver = CreateResolver(new HttpContextAccessor { HttpContext = context });
        var parser = UnitTestsHelpers.CreateModelParser();
        resolver.AddOrUpdate("", parser, "baseline layout", kind: VersionKind.Baseline);
        resolver.AddOrUpdate("feature/x", parser, "branch layout", kind: VersionKind.Branch);

        context.Request.Headers["Workflow-Version"] = "feature/x";
        Assert.Equal("branch layout", resolver.Resolve().DefaultMailLayout);

        context.Request.Headers["Workflow-Version"] = "missing";
        Assert.Equal("baseline layout", resolver.Resolve().DefaultMailLayout);

        context.Request.Headers.Remove("Workflow-Version");
        Assert.Equal("baseline layout", resolver.Resolve().DefaultMailLayout);
    }

    [Fact]
    public void ApiScope_UsesResolvedModelAndLayoutFromSameVersion()
    {
        var services = new ServiceCollection();
        services.AddWorkflowCore(new ConfigurationBuilder().Build());
        services.AddWorkflowApiCore();
        using var provider = services.BuildServiceProvider();
        var parser = UnitTestsHelpers.CreateModelParser();
        provider.GetRequiredService<ModelServiceResolver>()
            .AddOrUpdate("", parser, "baseline layout", kind: VersionKind.Baseline);

        using var scope = provider.CreateScope();

        Assert.Same(parser.WorkflowDefinitions,
            scope.ServiceProvider.GetRequiredService<ModelService>().WorkflowDefinitions);
        Assert.Equal("baseline layout",
            scope.ServiceProvider.GetRequiredService<MailTemplateStore>().Default);
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
        var loader = CreateLoader(CreateResolver(), new WorkflowSourceOptions { LocalPath = FixturesRoot });

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
        var requests = new List<Uri?>();
        var github = new FakeGitHub("sha-1", r => requests.Add(r.RequestUri));
        var resolver = CreateResolver();
        var loader = CreateLoader(resolver, RepoOptions(), github.Handler());

        Assert.True(await loader.LoadBaselineAsync());

        var version = Assert.Single(resolver.GetVersions());
        Assert.Equal("sha-1", version.Commit);
        Assert.True(loader.CanPoll);
        // First a ref -> SHA resolve on the API, then the resolved commit from codeload.
        Assert.Equal("api.github.com", requests[0]?.Host);
        Assert.Equal("codeload.github.com", requests[1]?.Host);
        Assert.EndsWith("/sha-1", requests[1]?.AbsolutePath);
    }

    [Fact]
    public async Task UnchangedBaseline_SkipsDownloadAndPreservesLoadedAt()
    {
        var hosts = new List<string>();
        var github = new FakeGitHub("sha-1", r => hosts.Add(r.RequestUri!.Host));
        var resolver = CreateResolver();
        var loader = CreateLoader(resolver, RepoOptions(), github.Handler());
        await loader.LoadBaselineAsync();
        var loadedAt = Assert.Single(resolver.GetVersions()).LoadedAt;
        hosts.Clear();

        Assert.False(await loader.ReloadBaselineIfChangedAsync());

        // Reload still resolves the SHA (which counts) but skips re-downloading the unchanged commit.
        Assert.Contains("api.github.com", hosts);
        Assert.DoesNotContain("codeload.github.com", hosts);
        Assert.Equal(loadedAt, Assert.Single(resolver.GetVersions()).LoadedAt);
    }

    [Fact]
    public async Task ChangedBaseline_InstallsNewCommit()
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
    public async Task InvalidChangedArchive_PreservesPreviousModel()
    {
        var github = new FakeGitHub("sha-1");
        var resolver = CreateResolver();
        var loader = CreateLoader(resolver, RepoOptions(), github.Handler());
        await loader.LoadBaselineAsync();
        var previous = Assert.Single(resolver.GetVersions());
        var previousConfig = resolver.Resolve();

        github.Sha = "sha-2";
        github.Archive = CreateInvalidArchive;
        // The bad install never advances the stored SHA, so each retry re-resolves against the last good one.
        await Assert.ThrowsAnyAsync<Exception>(() => loader.ReloadBaselineIfChangedAsync());
        var retained = Assert.Single(resolver.GetVersions());
        await Assert.ThrowsAnyAsync<Exception>(() => loader.ReloadBaselineIfChangedAsync());

        Assert.Equal(previous, retained);
        Assert.Same(previousConfig.ModelService.WorkflowDefinitions,
            resolver.Resolve().ModelService.WorkflowDefinitions);
        Assert.Equal(previousConfig.DefaultMailLayout, resolver.Resolve().DefaultMailLayout);
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
    public async Task LoadRef_PercentEncodesReservedCharactersInRef()
    {
        Uri? apiUri = null;
        var github = new FakeGitHub("resolved-sha",
            r =>
            {
                if (r.RequestUri!.Host == "api.github.com") apiUri = r.RequestUri;
            });

        await CreateLoader(CreateResolver(), RepoOptions(), github.Handler()).LoadBranchAsync("feature/#123");

        // '#' must be encoded, else the URL treats it as a fragment and GitHub only sees "feature/".
        Assert.EndsWith("/commits/feature/%23123", apiUri?.AbsoluteUri);
        Assert.Empty(apiUri?.Fragment ?? "");
    }

    [Fact]
    public async Task LoadBranch_StoresItsOwnLayout()
    {
        var context = new DefaultHttpContext();
        var resolver = CreateResolver(new HttpContextAccessor { HttpContext = context });
        var loader = CreateLoader(resolver, RepoOptions(), new FakeGitHub("sha-1").Handler());

        await loader.LoadBranchAsync("feature/x");
        context.Request.Headers["Workflow-Version"] = "feature/x";

        Assert.Equal(File.ReadAllText(Path.Combine(FixturesRoot, "Layouts", "default.html")),
            resolver.Resolve().DefaultMailLayout);
    }

    [Fact]
    public async Task LoadBranch_WithoutLayout_IsRejected()
    {
        var github = new FakeGitHub("sha-1") { Archive = CreateInvalidArchive };
        var resolver = CreateResolver();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            CreateLoader(resolver, RepoOptions(), github.Handler()).LoadBranchAsync("feature/x"));

        Assert.DoesNotContain(resolver.GetVersions(), version => version.Name == "feature/x");
    }

    [Fact]
    public async Task Upload_CheckoutRelativeFiles_InstallsProjectsAndLayout()
    {
        var context = new DefaultHttpContext();
        var resolver = CreateResolver(new HttpContextAccessor { HttpContext = context });

        var result = await CreateVersionsController(resolver).CreateVersion("upload", UploadFiles());
        context.Request.Headers["Workflow-Version"] = "upload";
        var config = resolver.Resolve();

        Assert.IsType<OkResult>(result);
        Assert.Contains("Project", config.ModelService.WorkflowDefinitions.Keys);
        Assert.Equal(File.ReadAllText(Path.Combine(FixturesRoot, "Layouts", "default.html")),
            config.DefaultMailLayout);
    }

    [Fact]
    public async Task Upload_WithoutLayout_IsRejected()
    {
        var resolver = CreateResolver();
        var files = UploadFiles();
        files.Remove("Layouts/default.html");

        var result = await CreateVersionsController(resolver).CreateVersion("upload", files);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.DoesNotContain(resolver.GetVersions(), version => version.Name == "upload");
    }

    [Fact]
    public async Task UnchangedPreview_SkipsDownloadAndPreservesLoadedAt()
    {
        var hosts = new List<string>();
        var github = new FakeGitHub("sha-1", r => hosts.Add(r.RequestUri!.Host));
        var resolver = CreateResolver();
        var loader = CreateLoader(resolver, RepoOptions(), github.Handler());
        await loader.LoadBranchAsync("feature/x");
        var loadedAt = Assert.Single(resolver.GetVersions()).LoadedAt;
        hosts.Clear();

        await loader.LoadBranchAsync("feature/x");

        Assert.DoesNotContain("codeload.github.com", hosts);
        Assert.Equal(loadedAt, Assert.Single(resolver.GetVersions()).LoadedAt);
    }

    [Fact]
    public async Task ManualAndBackgroundBaselineReloads_AreSerialized()
    {
        var active = 0;
        var maxActive = 0;
        var call = 0;
        var gate = new object();
        // The first resolve (call 1) installs sha-1; the concurrent reloads re-resolve, and we hold those
        // resolves open long enough to detect any overlap between the two reloads.
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            if (request.RequestUri!.Host == "codeload.github.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CreateArchive()) };

            if (Interlocked.Increment(ref call) > 1)
            {
                lock (gate)
                    maxActive = Math.Max(maxActive, ++active);
                await Task.Delay(50);
                lock (gate)
                    active--;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("sha-1") };
        });
        var loader = CreateLoader(CreateResolver(), RepoOptions(), handler);
        await loader.LoadBaselineAsync();

        await Task.WhenAll(loader.LoadBaselineAsync(), loader.ReloadBaselineIfChangedAsync());

        Assert.Equal(1, maxActive);
    }

    [Fact]
    public async Task LocalPathBaseline_DisablesPolling()
    {
        var loader = CreateLoader(CreateResolver(), new WorkflowSourceOptions { LocalPath = FixturesRoot });

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
        var loader = CreateLoader(CreateResolver(), new WorkflowSourceOptions { LocalPath = FixturesRoot });

        Assert.False(await loader.ReloadBaselineIfChangedAsync());
    }

    private static VersionsController CreateVersionsController(ModelServiceResolver resolver)
    {
        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["SystemAdmin"]);
        var rightsService = new RightsService(new ModelService(UnitTestsHelpers.CreateModelParser()),
            userService.Object, Mock.Of<IWorkflowInstanceRepository>());
        return new VersionsController(resolver,
            CreateLoader(resolver, new WorkflowSourceOptions { LocalPath = FixturesRoot }),
            rightsService, NullLogger<VersionsController>.Instance);
    }

    private static Dictionary<string, string> UploadFiles()
        => Directory.EnumerateFiles(FixturesRoot, "*.yaml", SearchOption.AllDirectories)
            .Append(Path.Combine(FixturesRoot, "Layouts", "default.html"))
            .ToDictionary(
                file => Path.GetRelativePath(FixturesRoot, file).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.Ordinal);

    // Stands in for GitHub: api.github.com resolves the ref to Sha (returned in the body); codeload.github.com
    // serves the current Archive for the resolved commit.
    private sealed class FakeGitHub(string sha, Action<HttpRequestMessage>? observe = null)
    {
        public string Sha { get; set; } = sha;
        public Func<byte[]> Archive { get; set; } = CreateArchive;

        public HttpMessageHandler Handler() => new StubHttpMessageHandler((request, _) =>
        {
            observe?.Invoke(request);
            return Task.FromResult(request.RequestUri!.Host == "api.github.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sha) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive()) });
        });
    }

    private static byte[] CreateArchive()
    {
        using var result = new MemoryStream();
        using (var gzip = new GZipStream(result, CompressionMode.Compress, leaveOpen: true))
            TarFile.CreateFromDirectory(FixturesRoot, gzip, includeBaseDirectory: true);
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