using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowAuthoring;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.Controllers;

public class WorkflowDraftsControllerTests
{
    private const string Folder = "Projects/Example";
    private const string FormPath = Folder + "/Forms/Start.yaml";
    private const string SecondFormPath = Folder + "/Forms/Followup.yaml";
    private const string FieldsPath = Folder + "/Fields.yaml";
    private const string Fields = "properties:\n  - name: Feedback\n    type: String\n    text: Old label\n";

    private const string Form =
        "pages:\n  - name: Start\n    title: Old page\n    elements:\n      - text: Old text\n      - question: Feedback\n      - question: Inherited\n";

    private static readonly string BaseSha = new('a', 40);
    private static readonly string HeadSha = new('b', 40);
    private static readonly string MergedSha = new('c', 40);
    private static readonly string UpdatedTargetSha = new('e', 40);
    private static readonly string Id = $"{BaseSha}-{new string('d', 32)}";

    [Fact]
    public async Task Draft_SurvivesLostResponsesThroughSaveProposalAndMerge()
    {
        using var github = new GitHub();
        var editor = Controller(github, "editor");
        var maintainer = Controller(github, "maintainer");
        var ct = CancellationToken.None;

        var available = Assert.Single(await editor.List(ct));
        Assert.Equal("example", available.Id);
        Assert.True(available.CanEdit);
        Assert.False(available.CanMerge);
        Assert.Empty((await editor.ListDrafts("example", ct)).Drafts);
        var draft = await editor.Open("example", Id, ct);
        Assert.Equal(BaseSha, draft.BaseSha);
        var baseCommitUrl = $"https://github.com/example/config/commit/{BaseSha}";
        Assert.Equal(baseCommitUrl, draft.BaseCommitUrl);
        Assert.Equal(BaseSha, draft.HeadSha);
        var started = Assert.Single((await editor.ListDrafts("example", ct)).Drafts);
        Assert.Equal(Id, started.Id);
        Assert.False(started.HasSavedChanges);
        Assert.Null(started.UpdatedAt);
        Assert.Equal(draft, await editor.Open("example", Id, ct));

        var save = new DraftWorkspaceInput(BaseSha, new() { [FormPath] = Form.Replace("Old text", "New help text") },
            []);
        await Error(503, editor.Save("example", Id, save, ct));
        await Error(409,
            editor.Save("example", Id,
                save with { Files = new() { [FormPath] = Form.Replace("Old text", "Different edit") } }, ct));
        draft = (await editor.Save("example", Id, save, ct)).Draft;
        Assert.Equal(HeadSha, draft.HeadSha);
        Assert.Equal(baseCommitUrl, draft.BaseCommitUrl);
        Assert.Equal(draft, await editor.Get("example", Id, ct));
        var preview = await editor.Preview("example", Id, FormPath, new DraftWorkspaceInput(HeadSha, [], []), ct);
        Assert.Equal("New help text", preview.Pages[0].Elements[0].Text?.En);
        Assert.Equal(1, github.Saves);

        var head = new DraftHeadInput(HeadSha);
        await Error(503, editor.Propose("example", Id, head, ct));
        await Error(403, editor.Merge("example", Id, head, ct));
        draft = await editor.Propose("example", Id, head, ct);
        Assert.Equal(1, github.Proposals);
        Assert.Equal(42, draft.PullRequest?.Number);
        var listed = Assert.Single((await editor.ListDrafts("example", ct)).Drafts);
        Assert.True(listed.HasSavedChanges);
        Assert.Equal(DateTimeOffset.Parse("2026-11-19T10:00:00Z"), listed.UpdatedAt);
        Assert.Equal("open", listed.PullRequest?.State);
        await Error(409, maintainer.Merge("example", Id, new DraftHeadInput(BaseSha), ct));

        await Error(503, maintainer.Merge("example", Id, head, ct));
        draft = await maintainer.Merge("example", Id, head, ct);
        Assert.Equal(MergedSha, draft.PullRequest?.MergedSha);
        Assert.Equal("merged", draft.PullRequest?.State);
        Assert.Equal(1, github.Merges);
        Assert.Empty((await editor.ListDrafts("example", ct)).Drafts);
    }

    [Fact]
    public async Task DraftForm_PreviewsPendingEditsAndSavesOnlyTheAllowedFilesTogether()
    {
        using var github = new GitHub { Head = BaseSha, LoseSaveResponse = false };
        var editor = Controller(github, "editor");
        var maintainer = Controller(github, "maintainer");
        var ct = CancellationToken.None;

        var opened = await editor.GetWorkspace("example", Id, ct);
        Assert.Equal([SecondFormPath, FormPath], opened.Forms.Select(form => form.Path));
        Assert.DoesNotContain("Projects/Base/Entity.yaml", opened.EditablePaths);
        Assert.Empty((await maintainer.GetWorkspace("example", Id, ct)).Files);

        var input = new DraftWorkspaceInput(BaseSha, new()
        {
            [FormPath] = "title:\n  en: Review form\n  nl: Beoordelingsformulier\nlayout: Compact\n" +
                         Form.Replace("Old page", "New page").Replace("- text: Old text",
                             "- text:\n          en: New help\n          nl: Nieuwe hulp"),
            [FieldsPath] = Fields.Replace("Old label", "New label")
        }, []);
        await Error(403, editor.Preview("other", Id, FormPath, input, ct));
        await Error(403, maintainer.Preview("example", Id, FormPath, input, ct));
        await Error(403, editor.Save("example", Id,
            input with { Files = new() { ["Projects/Base/Entity.yaml"] = "name: OutOfScope" } }, ct));
        await Error(403, editor.Save("example", Id,
            input with { Files = new() { [FormPath] = input.Files[FormPath] + "repeatable: true\n" } }, ct));

        var preview = await editor.Preview("example", Id, FormPath, input, ct);
        Assert.Equal("Start", preview.Name);
        Assert.Equal("Review form", preview.Title.En);
        Assert.Equal("Beoordelingsformulier", preview.Title.Nl);
        Assert.Equal(FormLayout.Compact, preview.Layout);
        Assert.Equal("New page", Assert.Single(preview.Pages).Title.En);
        Assert.Equal("New help", preview.Pages[0].Elements[0].Text?.En);
        Assert.Equal("Nieuwe hulp", preview.Pages[0].Elements[0].Text?.Nl);
        Assert.Equal("New label", preview.Pages[0].Elements[1].Question?.Text.En);
        Assert.Equal(BaseSha, github.Head);
        Assert.Equal(0, github.Saves);

        var saved = await editor.Save("example", Id, input, ct);
        Assert.Equal(HeadSha, saved.Draft.HeadSha);
        Assert.Equal((string[])[FieldsPath, FormPath], github.ChangedPaths.Order().ToArray());
        var reopened = await editor.GetWorkspace("example", Id, ct);
        var reopenedForm = Assert.Single(reopened.Forms, item => item.Path == FormPath);
        Assert.Equal("Start", reopenedForm.Name);
        Assert.Equal("Review form", reopenedForm.Title.En);
        Assert.Equal(FormLayout.Compact, reopenedForm.Layout);
        Assert.Contains("Nieuwe hulp", reopened.Files[FormPath]);
        Assert.Equal("Followup", (await editor.Preview("example", Id, SecondFormPath,
            new DraftWorkspaceInput(HeadSha, [], []), ct)).Name);
        var change = Assert.Single(await maintainer.GetFormChanges("example", Id, HeadSha, ct));
        Assert.Equal(FormPath, change.Path);
        Assert.Equal("Old label", change.Before?.Pages[0].Elements[1].Question?.Text.En);
        Assert.Equal("New label", change.After?.Pages[0].Elements[1].Question?.Text.En);
        await Error(409, maintainer.GetFormChanges("example", Id, BaseSha, ct));
        await Error(403, Controller(github, "other-editor").GetFormChanges("example", Id, HeadSha, ct));
        await Error(409, editor.Preview("example", Id, FormPath, input, ct));
        await Error(503, editor.Propose("example", Id, new DraftHeadInput(HeadSha), ct));
        Assert.Equal(1, github.Proposals);
        await Error(503, maintainer.Merge("example", Id, new DraftHeadInput(HeadSha), ct));
        Assert.Equal(1, github.Merges);
    }

    [Fact]
    public async Task DraftForm_EditsCalloutPresentationWithoutChangingItsCondition()
    {
        const string callout = """
                                     - callout:
                                         variant: warning
                                         title: Submission rule
                                         text: |
                                           Old **Markdown**.
                                         condition:
                                           value:
                                             property: Feedback
                                             equal: =Existing
                               """;
        var second = callout.Replace("Submission rule", "Another rule").Replace("=Existing", "=Other");
        var original = Form + callout + "\n" + second + "\n";
        var edited = Form + second + "\n" + callout.Replace("variant: warning", "variant: note")
            .Replace("Submission rule", "Submission guidance")
            .Replace("Old **Markdown**.", "New **Markdown**.") + "\n";
        using var github = new GitHub { Head = BaseSha, LoseSaveResponse = false, BaseForm = original };
        var editor = Controller(github, "editor");
        var ct = CancellationToken.None;

        DraftWorkspaceInput Input(string value) => new(BaseSha, new() { [FormPath] = value }, []);

        await Error(403, editor.Save("example", Id, Input(edited.Replace("=Existing", "=Different")), ct));
        await Error(403, editor.Save("example", Id, Input(edited.Replace("New **Markdown**.", "{{ Feedback }}")), ct));
        var result = await editor.Preview("example", Id, FormPath, Input(edited), ct);
        var callouts = result.Pages[0].Elements.Select(element => element.Callout).OfType<CalloutDto>().ToArray();
        Assert.Equal(["Another rule", "Submission guidance"], callouts.Select(item => item.Title?.En));
        var preview = callouts[1];
        Assert.Equal(CalloutVariant.Note, preview.Variant);
        Assert.Equal("Submission guidance", preview.Title?.En);
        Assert.Equal("New **Markdown**.\n", preview.Text?.En);

        var saved = await editor.Save("example", Id, Input(edited), ct);
        Assert.Equal(edited, saved.Files[FormPath]);
        Assert.Equal(1, github.Saves);
    }

    [Fact]
    public async Task Draft_RequiresProjectGrantAndRejectsChangesToOtherFiles()
    {
        using var github = new GitHub { ExtraFile = true, Head = HeadSha };
        var editor = Controller(github, "editor");
        var ct = CancellationToken.None;

        await Error(403, editor.Save("other", Id, new DraftWorkspaceInput(BaseSha, [], []), ct));
        Assert.Equal(0, github.Requests);
        await Error(403, editor.Propose("example", Id, new DraftHeadInput(HeadSha), ct));
        Assert.Equal(0, github.Proposals);
    }

    [Fact]
    public async Task Discard_RejectsStaleOrUnauthorizedRequestsThenClosesAndDeletesWithRetry()
    {
        using var github = new GitHub { Head = HeadSha, FailDeleteOnce = true };
        var editor = Controller(github, "editor");
        var ct = CancellationToken.None;
        await Error(503, editor.Propose("example", Id, new DraftHeadInput(HeadSha), ct));
        var input = new DiscardDraftInput(HeadSha, 42);
        await Error(403, Controller(github, "maintainer").Discard("example", Id, input, ct));
        await Error(409, editor.Discard("example", Id, input with { ExpectedHeadSha = BaseSha }, ct));
        await Error(409, editor.Discard("example", Id, input with { PullRequestNumber = null }, ct));
        Assert.False(github.Closed);
        Assert.Equal(0, github.Deletes);

        await Error(503, editor.Discard("example", Id, input, ct));
        Assert.True(github.Closed);
        Assert.Equal(HeadSha, github.Head);
        Assert.True(await editor.Discard("example", Id, input, ct));
        Assert.False((await editor.Get("example", Id, ct)).HasBranch);
        Assert.Empty((await editor.ListDrafts("example", ct)).Drafts);
        Assert.True(await editor.Discard("example", Id, input, ct));
        Assert.Equal(1, github.Deletes);
    }

    [Fact]
    public async Task Discard_DeletesAnUnproposedDraftAndProtectsAMergedDraft()
    {
        using var github = new GitHub { Head = BaseSha };
        var editor = Controller(github, "editor");
        var ct = CancellationToken.None;
        Assert.True(await editor.Discard("example", Id, new DiscardDraftInput(BaseSha, null), ct));
        Assert.Null(github.Head);
        Assert.False(github.Closed);

        github.Head = HeadSha;
        var head = new DraftHeadInput(HeadSha);
        await Error(503, editor.Propose("example", Id, head, ct));
        await Error(503, Controller(github, "maintainer").Merge("example", Id, head, ct));
        await Error(409, editor.Discard("example", Id, new DiscardDraftInput(HeadSha, 42), ct));
        Assert.Equal(1, github.Deletes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discard_PreservesASaveThatLandsJustBeforeDeletion(bool inReview)
    {
        var newerHead = new string('f', 40);
        using var github = new GitHub { Head = HeadSha, HeadAtDelete = newerHead };
        var editor = Controller(github, "editor");
        var ct = CancellationToken.None;
        if (inReview)
            await Error(503, editor.Propose("example", Id, new DraftHeadInput(HeadSha), ct));

        var error = await Assert.ThrowsAsync<WorkflowAuthoringException>(() =>
            editor.Discard("example", Id, new DiscardDraftInput(HeadSha, inReview ? 42 : null), ct));

        Assert.Equal(409, error.Status);
        Assert.Equal("DraftHeadChanged", error.Code);
        Assert.Equal(newerHead, github.Head);
        Assert.Equal(0, github.Deletes);
    }

    [Fact]
    public async Task DraftForm_PreviewsAndSavesQuestionSettings()
    {
        using var github = new GitHub { Head = BaseSha, LoseSaveResponse = false };
        var editor = Controller(github, "editor");
        var ct = CancellationToken.None;
        var input = new DraftWorkspaceInput(BaseSha, new()
        {
            [FieldsPath] = Fields.Replace("type: String", "type: Feedback!") +
                           "    values:\n      - name: Yes\n        text: Yes\n      - name: No\n        text: No\n    control: Dropdown\n"
        }, []);
        var advanced = input with
        {
            Files = new() { [FieldsPath] = Fields + "    shortText: Forbidden\n" },
            YamlFiles = [FieldsPath]
        };
        await Error(403, editor.Save("example", Id, advanced, ct));

        var preview = await editor.Preview("example", Id, FormPath, input, ct);
        Assert.True(preview.Pages[0].Elements[1].Question!.IsRequired);
        Assert.Equal(2, Assert.IsType<ChoiceQuestionDto>(preview.Pages[0].Elements[1].Question).Choices.Length);
        Assert.Equal(0, github.Saves);
        var saved = await editor.Save("example", Id, input, ct);
        Assert.Equal(HeadSha, saved.Draft.HeadSha);
        Assert.Contains("control: Dropdown", (await editor.GetWorkspace("example", Id, ct)).Files[FieldsPath]);
        Assert.Equal(FieldsPath, Assert.Single(github.ChangedPaths));
        await Error(503, editor.Propose("example", Id, new DraftHeadInput(HeadSha), ct));
        Assert.Equal(1, github.Proposals);
    }

    [Fact]
    public async Task DraftForm_RejectsRepositoryChangesToAdvancedPropertySettings()
    {
        using var github = new GitHub { Head = HeadSha, OverrideFields = Fields + "    shortText: Forbidden\n" };
        github.ChangedPaths.Add(FieldsPath);
        var editor = Controller(github, "editor");

        await Error(403, editor.Propose("example", Id, new DraftHeadInput(HeadSha), CancellationToken.None));
        Assert.Equal(0, github.Proposals);
    }

    [Fact]
    public async Task DraftForm_ChecksWhetherTheCurrentTargetSharesTheDefinition()
    {
        using var github = new GitHub { Head = HeadSha, Target = UpdatedTargetSha };
        var editor = Controller(github, "editor");
        var maintainer = Controller(github, "maintainer");
        var head = new DraftHeadInput(HeadSha);
        var ct = CancellationToken.None;

        await Error(403, editor.Propose("example", Id, head, ct));
        Assert.Equal(0, github.Proposals);
        github.Target = BaseSha;
        await Error(503, editor.Propose("example", Id, head, ct));

        github.Target = UpdatedTargetSha;
        await Error(403, maintainer.Merge("example", Id, head, ct));
        Assert.Equal(0, github.Merges);
    }

    [Fact]
    public async Task AuthoringIgnoresTheRuntimeVersionHeader()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) };
        http.Request.Headers[ModelServiceResolver.VersionHeader] = HeadSha;
        var action = new ActionContext(http, new RouteData(), new ControllerActionDescriptor
            { ControllerTypeInfo = typeof(WorkflowDraftsController).GetTypeInfo() });
        var resolver = new ModelServiceResolver(new HttpContextAccessor());
        var filter = new WorkflowVersionFilter(resolver, null!);
        var reachedController = false;

        await filter.OnResourceExecutionAsync(new ResourceExecutingContext(action, [], []), () =>
        {
            reachedController = true;
            return Task.FromResult(new ResourceExecutedContext(action, []));
        });

        Assert.True(reachedController);
    }

    [Fact]
    public async Task WorkflowPreview_UsesPendingEditsForItsOwnerAndRejectsWrites()
    {
        using var github = new GitHub { Head = BaseSha };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var editor = Controller(github, "editor", cache: cache);
        var input = new DraftWorkspaceInput(BaseSha, new() { [FormPath] = Form.Replace("Old page", "Preview only") },
            []);
        var preview = await editor.PreviewWorkflow("example", Id, input, CancellationToken.None);

        var workflowOptions = Options.Create(new WorkflowAuthoringOptions
        {
            Enabled = true,
            Projects = new() { ["example"] = new() { Title = "Example", Folder = Folder } },
            Grants = new() { [Folder] = new() { EditorUserIds = ["editor", "other-editor"] } }
        });
        var accessor = new HttpContextAccessor();
        var resolver = new ModelServiceResolver(accessor);
        resolver.AddOrUpdate("", new ModelParser(new DictionaryProvider(new()
        {
            ["Projects/Baseline/Entity.yaml"] = "name: Baseline"
        })), "baseline");

        await Invoke(HttpMethods.Get, "editor",
            model =>
            {
                Assert.Equal("Preview only",
                    model.WorkflowDefinitions["Example"].Forms.Single(form => form.Name == "Start").Pages[0].Title!.En);
            });

        await Error(403, Invoke(HttpMethods.Post, "editor", _ => Assert.Fail("Preview performed a write.")));
        await Error(403,
            Invoke(HttpMethods.Get, "other-editor", _ => Assert.Fail("Preview leaked to another editor.")));
        Assert.Equal(0, github.Saves);

        async Task Invoke(string method, string userId, Action<ModelService> next)
        {
            using var services = new ServiceCollection()
                .AddSingleton<IMemoryCache>(cache)
                .AddSingleton(workflowOptions)
                .AddSingleton(Mock.Of<IWebHostEnvironment>(host => host.EnvironmentName == "Test"))
                .AddSingleton(Mock.Of<IUserService>(users => users.GetRealUser(It.IsAny<CancellationToken>()) ==
                                                             Task.FromResult<User?>(new User { Id = userId })))
                .BuildServiceProvider();
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity("test")),
                RequestServices = services
            };
            http.Request.Method = method;
            http.Request.Headers[ModelServiceResolver.VersionHeader] = preview.Version;
            accessor.HttpContext = http;
            var model = resolver.Resolve().ModelService;
            Assert.Contains("Baseline", model.WorkflowDefinitions.Keys);
            var action = new ActionContext(http, new RouteData(), new ControllerActionDescriptor
                { ControllerTypeInfo = typeof(ControllerBase).GetTypeInfo() });
            await new WorkflowVersionFilter(resolver, null!).OnResourceExecutionAsync(
                new ResourceExecutingContext(action, [], []), () =>
                {
                    next(model);
                    return Task.FromResult(new ResourceExecutedContext(action, []));
                });
        }
    }

    [Fact]
    public async Task Draft_PreviewRequiresFormInSavedArchive()
    {
        using var github = new GitHub { Head = HeadSha, MissingArchiveForm = true };
        var editor = Controller(github, "editor");

        await Error(404, editor.Preview("example", Id, FormPath, new DraftWorkspaceInput(HeadSha, [], []),
            CancellationToken.None));
    }

    [Fact]
    public async Task Draft_DisablesAuthoringDuringImpersonationAndInProduction()
    {
        using var github = new GitHub();
        var editor = Controller(github, "editor");
        var production = Controller(github, "editor", "Production");
        var ct = CancellationToken.None;
        editor.Request.Headers["X-User-Impersonation"] = "preview";
        Assert.Empty(await editor.List(ct));
        Assert.Empty(await production.List(ct));
        await Error(403, editor.Open("example", Id, ct));
        Assert.Equal(0, github.Requests);
    }

    private static async Task Error(int status, Task operation)
    {
        var error = await Assert.ThrowsAsync<WorkflowAuthoringException>(() => operation);
        Assert.Equal(status, error.Status);
    }

    private static WorkflowDraftsController Controller(GitHub github, string actor, string environment = "Test",
        IMemoryCache? cache = null)
    {
        using var key = RSA.Create(2048);
        var settings = Options.Create(new WorkflowAuthoringOptions
        {
            Enabled = true,
            RepoUrl = "https://github.com/example/config",
            TargetBranch = "sandbox",
            GitHubAppId = "12345",
            GitHubInstallationId = 6,
            GitHubPrivateKey = key.ExportRSAPrivateKeyPem(),
            Grants = new()
            {
                ["Projects"] = new() { MaintainerUserIds = ["maintainer"] },
                [Folder] = new() { EditorUserIds = ["editor"] },
                ["Projects/Other"] = new() { EditorUserIds = ["other-editor"] }
            },
            Projects = new Dictionary<string, WorkflowAuthoringProject>
            {
                ["example"] = new()
                {
                    Title = "Example", Folder = Folder
                },
                ["other"] = new()
                {
                    Title = "Other", Folder = "Projects/Other"
                }
            }
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(clientFactory => clientFactory.CreateClient(It.IsAny<string>())).Returns(github.NewClient);
        var clients = factory.Object;
        var git = new GitHubWorkflowDrafts(clients, new GitHubAppToken(clients, settings), settings);
        var service = new WorkflowDraftService(git, cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<WorkflowDraftService>.Instance);
        return new WorkflowDraftsController(service, git,
            Mock.Of<IUserService>(users => users.GetRealUser(It.IsAny<CancellationToken>()) ==
                                           Task.FromResult<User?>(new User { Id = actor })),
            Mock.Of<IWebHostEnvironment>(host => host.EnvironmentName == environment), settings)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private sealed class GitHub : HttpMessageHandler
    {
        private const string Branch = "builder/example/";
        private string _savedFields = Fields;
        private string _savedForm = Form.Replace("Old text", "Existing edit");
        public int Requests { get; private set; }
        public int Saves { get; private set; }
        public int Proposals { get; private set; }
        public int Merges { get; private set; }
        public int Deletes { get; private set; }
        public bool Closed { get; private set; }
        public bool FailDeleteOnce { get; set; }
        public string? HeadAtDelete { get; init; }
        public string? Head { get; set; }
        public string Target { get; set; } = BaseSha;
        public bool ExtraFile { get; init; }
        public bool LoseSaveResponse { get; init; } = true;
        public List<string> ChangedPaths { get; } = [];
        public bool MissingArchiveForm { get; init; }
        public string? OverrideFields { get; init; }
        public string BaseForm { get; init; } = Form;

        public HttpClient NewClient()
        {
            return new(this, disposeHandler: false)
                { BaseAddress = new Uri("https://api.github.com/") };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var path = request.RequestUri!.AbsolutePath;
            var operation = request.Method.Method;
            if (path.EndsWith("/access_tokens", StringComparison.Ordinal))
                return Json(new { token = "installation-token", expires_at = DateTimeOffset.UtcNow.AddHours(1) });
            if (path == "/repos/example/config" && operation == "GET")
                return Json(new { node_id = "repository-id" });
            if (path.EndsWith("/git/ref/heads/sandbox", StringComparison.Ordinal))
                return Json(new { @object = new { sha = Target } });
            if (path.EndsWith($"/git/ref/heads/{Branch}{Id}", StringComparison.Ordinal))
                return Head is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json(new { @object = new { sha = Head } });
            if (path.EndsWith("/git/refs", StringComparison.Ordinal) && operation == "POST")
            {
                Head = BaseSha;
                return Json(new { @object = new { sha = Head } });
            }

            if (path.EndsWith("/graphql", StringComparison.Ordinal))
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var variables = payload.RootElement.GetProperty("variables");
                if (!variables.TryGetProperty("input", out var input))
                {
                    Assert.Equal(Branch, variables.GetProperty("prefix").GetString());
                    Assert.Equal("refs/heads/sandbox", variables.GetProperty("qualifiedTarget").GetString());
                    return DraftList();
                }

                if (input.TryGetProperty("refUpdates", out var updates))
                {
                    var update = Assert.Single(updates.EnumerateArray());
                    Assert.Equal("repository-id", input.GetProperty("repositoryId").GetString());
                    Assert.Equal($"refs/heads/{Branch}{Id}", update.GetProperty("name").GetString());
                    Assert.Equal(new string('0', 40), update.GetProperty("afterOid").GetString());
                    Assert.True(Proposals == 0 || Closed);
                    if (FailDeleteOnce)
                    {
                        FailDeleteOnce = false;
                        throw new HttpRequestException("Branch deletion is temporarily unavailable.");
                    }

                    Head = HeadAtDelete ?? Head;
                    if (Head is null || Head != update.GetProperty("beforeOid").GetString())
                        return Json(new { errors = new[] { new { message = "The reference changed." } } });

                    Head = null;
                    Deletes++;
                    return Json(new { data = new { updateRefs = new { clientMutationId = (string?)null } } });
                }

                Assert.Equal(BaseSha, input.GetProperty("expectedHeadOid").GetString());
                Assert.Equal(Branch + Id, input.GetProperty("branch").GetProperty("branchName").GetString());
                foreach (var addition in input.GetProperty("fileChanges").GetProperty("additions").EnumerateArray())
                {
                    var changedPath = addition.GetProperty("path").GetString()!;
                    var content =
                        Encoding.UTF8.GetString(
                            Convert.FromBase64String(addition.GetProperty("contents").GetString()!));
                    ChangedPaths.Add(changedPath);
                    if (changedPath == FormPath)
                        _savedForm = content;
                    else if (changedPath == FieldsPath)
                        _savedFields = content;
                    else
                        throw new InvalidOperationException($"Unexpected change to {changedPath}");
                }

                Head = HeadSha;
                Saves++;
                if (LoseSaveResponse)
                    throw new HttpRequestException("Save accepted before connection closed.");

                return Json(new { data = new { createCommitOnBranch = new { commit = new { oid = HeadSha } } } });
            }

            if (path.EndsWith($"/git/commits/{HeadSha}", StringComparison.Ordinal))
                return Json(new { parents = new[] { new { sha = BaseSha } } });
            if (path.EndsWith($"/compare/{Target}...{HeadSha}", StringComparison.Ordinal))
                return Json(new
                {
                    merge_base_commit = new { sha = BaseSha },
                    files = (ExtraFile ? ["Common/Templates/Shared.yaml"] :
                            ChangedPaths.Count == 0 ? [FormPath] : ChangedPaths)
                        .Select(filename => new { filename, status = "modified" }).ToArray()
                });
            var sha = path.Split('/')[^1];
            if (path == $"/repos/example/config/tarball/{sha}" &&
                (sha == BaseSha || sha == HeadSha || sha == UpdatedTargetSha))
            {
                Assert.Equal("installation-token", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new ByteArrayContent(Archive(sha == HeadSha, sha == UpdatedTargetSha)) };
            }

            if (path.EndsWith("/pulls", StringComparison.Ordinal) && operation == "GET")
                return Json(Proposals == 0 ? Array.Empty<object>() : [PullRequest()]);
            if (path.EndsWith("/pulls", StringComparison.Ordinal) && operation == "POST")
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("sandbox", payload.RootElement.GetProperty("base").GetString());
                Proposals++;
                throw new HttpRequestException("PR accepted before connection closed.");
            }

            if (path.EndsWith("/pulls/42/merge", StringComparison.Ordinal))
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(HeadSha, payload.RootElement.GetProperty("sha").GetString());
                Assert.Equal("squash", payload.RootElement.GetProperty("merge_method").GetString());
                Merges++;
                Head = null;
                throw new HttpRequestException("Merge accepted before connection closed.");
            }

            if (path.EndsWith("/pulls/42", StringComparison.Ordinal) && operation == "PATCH")
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal("closed", payload.RootElement.GetProperty("state").GetString());
                Assert.False(Closed);
                Closed = true;
                return Json(PullRequest());
            }

            throw new InvalidOperationException($"Unexpected GitHub request {operation} {path}");
        }

        private HttpResponseMessage DraftList()
        {
            var pullRequests = Proposals == 0
                ? Array.Empty<object>()
                :
                [
                    new
                    {
                        state = Closed ? "CLOSED" : "OPEN",
                        updatedAt = "2026-11-19T10:00:00Z", headRefName = Branch + Id,
                        headRepository = new { nameWithOwner = "example/config" }
                    }
                ];
            var nodes = Head is null
                ? Array.Empty<object>()
                :
                [
                    new
                    {
                        name = Branch + Id, target = new { oid = Head, committedDate = "2026-11-18T10:00:00Z" },
                        associatedPullRequests = new { nodes = pullRequests }
                    }
                ];
            return Json(new
            {
                data = new
                {
                    repository = new
                    {
                        target = new { target = new { oid = Target } },
                        refs = new { nodes, pageInfo = new { hasNextPage = false } }
                    }
                }
            });
        }

        private object PullRequest()
        {
            return new
            {
                number = 42, html_url = "https://github.com/example/config/pull/42",
                state = Merges > 0 || Closed ? "closed" : "open",
                merged_at = Merges > 0 ? "2026-11-18T10:00:00Z" : null,
                merge_commit_sha = Merges > 0 ? MergedSha : null,
                head = new { @ref = Branch + Id, sha = HeadSha, repo = new { full_name = "example/config" } }
            };
        }

        private byte[] Archive(bool draft, bool shared = false)
        {
            using var bytes = new MemoryStream();
            using (var gzip = new GZipStream(bytes, CompressionMode.Compress, leaveOpen: true))
            using (var tar = new TarWriter(gzip))
            {
                Write("Projects/Base/Entity.yaml",
                    "name: Base\ntitlePlural: Examples\nproperties:\n  - name: Inherited\n    type: String\n    text: Shared label\n");
                Write(Folder + "/Entity.yaml",
                    "name: Example\ninheritsFrom: Base\ntitlePlural: Examples\nproperties: []\n");
                if (shared)
                    Write("Projects/Other/Entity.yaml", "name: Other\ninheritsFrom: Example\ntitlePlural: Others\n");
                Write(FieldsPath, draft ? OverrideFields ?? _savedFields : Fields);
                Write(SecondFormPath, "pages:\n  - name: Followup\n    elements:\n      - text: Follow-up text\n");
                if (!MissingArchiveForm)
                    Write(FormPath, draft ? _savedForm : BaseForm);

                void Write(string path, string text)
                {
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "checkout/" + path)
                    {
                        DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text))
                    });
                }
            }

            return bytes.ToArray();
        }

        private static HttpResponseMessage Json(object payload)
        {
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        }
    }
}