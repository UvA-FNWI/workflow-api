using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
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

    private static IMemoryCache CreateCache() => new MemoryCache(new MemoryCacheOptions());

    private static WorkflowConfigLoader CreateLoader(ModelServiceResolver resolver, WorkflowSourceOptions opts)
        => new(new Mock<IHttpClientFactory>().Object, resolver, Options.Create(opts),
            new MailTemplateStore(), CreateCache(), NullLogger<WorkflowConfigLoader>.Instance);

    [Fact]
    public async Task LoadBaseline_FromLocalPath_RegistersBaselineWithDefinitions()
    {
        var opts = new WorkflowSourceOptions { LocalPath = FixturesPath };
        var resolver = CreateResolver();

        await CreateLoader(resolver, opts).LoadBaselineAsync();

        Assert.True(resolver.Get().WorkflowDefinitions.ContainsKey("Project"));
        Assert.Contains(resolver.GetVersions(), v => v.Name == "");
    }

    [Fact]
    public async Task LoadBaseline_NoSourceConfigured_Throws()
    {
        var opts = new WorkflowSourceOptions(); // no LocalPath, no RepoUrl

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateLoader(CreateResolver(), opts).LoadBaselineAsync());
    }

    [Fact]
    public async Task LoadBranch_WithoutRepoUrl_Throws()
    {
        var opts = new WorkflowSourceOptions { LocalPath = FixturesPath };
        var loader = CreateLoader(CreateResolver(), opts);

        await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadBranchAsync("feature/x"));
    }

    [Fact]
    public async Task LoadBranch_InvalidRef_Throws()
    {
        var opts = new WorkflowSourceOptions { RepoUrl = "https://github.com/uva/milestones-config" };
        var loader = CreateLoader(CreateResolver(), opts);

        await Assert.ThrowsAsync<ArgumentException>(() => loader.LoadBranchAsync("x/../../other/repo"));
    }

    private static WorkflowConfigLoader FetchFailingLoader(ModelServiceResolver resolver, WorkflowSourceOptions opts)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Throws(new HttpRequestException("boom"));
        return new WorkflowConfigLoader(factory.Object, resolver, Options.Create(opts),
            new MailTemplateStore(), CreateCache(), NullLogger<WorkflowConfigLoader>.Instance);
    }

    [Fact]
    public async Task LoadBaseline_FetchFails_Throws()
    {
        var opts = new WorkflowSourceOptions { RepoUrl = "https://github.com/uva/milestones-config" };
        var loader = FetchFailingLoader(CreateResolver(), opts);

        await Assert.ThrowsAsync<HttpRequestException>(() => loader.LoadBaselineAsync());
    }
}