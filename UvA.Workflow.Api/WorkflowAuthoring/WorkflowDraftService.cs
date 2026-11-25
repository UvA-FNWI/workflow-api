using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using UvA.Workflow.Api.Submissions.Dtos;

namespace UvA.Workflow.Api.WorkflowAuthoring;

public sealed record WorkflowDraft(
    string Id,
    string BaseSha,
    string BaseCommitUrl,
    string HeadSha,
    DraftPullRequest? PullRequest,
    bool HasBranch);

public sealed record WorkflowPreview(string Version, string Definition, string? Screen);

internal sealed record WorkflowPreviewSnapshot(ModelParser Parser, string Project, string UserId)
{
    internal const string Prefix = "draft-preview-";
}

public sealed class WorkflowDraftService(
    GitHubWorkflowDrafts git,
    IMemoryCache cache,
    ILogger<WorkflowDraftService> logger)
{
    public static bool IsDraftId(string id)
    {
        return Regex.IsMatch(id, @"\A[0-9a-f]{40}-[0-9a-f]{32}\z");
    }

    private static string Branch(string projectId, string draftId)
    {
        return $"builder/{projectId}/{draftId}";
    }

    public async Task<WorkflowDraft> Open(string projectId, string id, WorkflowAuthoringProject project,
        CancellationToken ct)
    {
        var branch = Branch(projectId, id);
        var baseSha = id[..40];
        if (await git.GetHead(branch, ct) is null && await git.FindPullRequest(branch, ct) is null)
        {
            if (baseSha != await git.GetBase(ct))
                throw Conflict("DraftBaseChanged", "The target branch moved. Start a draft from its latest revision.");

            var workspace = await ReadWorkspace(baseSha, project, ct);
            if (!workspace.HasEditableForms)
                throw Conflict("NoEditableForms", "This workflow has no editable local forms.");
            await git.Create(branch, baseSha, ct);
        }

        return await Get(projectId, id, ct);
    }

    public Task<WorkflowDraft> Get(string projectId, string id, CancellationToken ct)
        => Load(Branch(projectId, id), id, ct);

    public async Task<WorkflowDraftWorkspace> GetWorkspace(string projectId, string id,
        WorkflowAuthoringProject project,
        bool canEdit, CancellationToken ct)
    {
        var draft = await Get(projectId, id, ct);
        var workspace = await ReadWorkspace(draft.HeadSha, project, ct);
        return workspace.View(draft, canEdit && draft.PullRequest is not { State: not "open" });
    }

    public async Task<DraftFormChange[]> GetFormChanges(string projectId, string id, WorkflowAuthoringProject project,
        string expectedHead, CancellationToken ct)
    {
        var draft = await Get(projectId, id, ct);
        EnsureHead(draft, expectedHead);
        if (draft.BaseSha == draft.HeadSha)
            return [];

        var baseTask = ReadWorkspace(draft.BaseSha, project, ct);
        var headTask = ReadWorkspace(draft.HeadSha, project, ct);
        await Task.WhenAll(baseTask, headTask);
        var baseline = await baseTask;
        var candidate = await headTask;
        return Edit(() => baseline.CompareForms(candidate));
    }

    public async Task<WorkflowDraftWorkspace> Save(string projectId, string id, WorkflowAuthoringProject project,
        DraftWorkspaceInput input, string actorId, CancellationToken ct)
    {
        var branch = Branch(projectId, id);
        var draft = await Load(branch, id, ct);
        EnsureOpen(draft);
        if (draft.HeadSha != input.ExpectedHeadSha)
            return await RecoverSave(draft);

        var current = await ReadWorkspace(draft.HeadSha, project, ct);
        var (candidate, changes) = Apply(current, input);
        if (changes.Count == 0)
            return current.View(draft, true);

        var committed = await git.Save(branch, input.ExpectedHeadSha, changes, ct);
        if (committed is null)
        {
            var latest = await Load(branch, id, ct);
            EnsureOpen(latest);
            if (latest.HeadSha != input.ExpectedHeadSha)
                return await RecoverSave(latest);

            throw new HttpRequestException("GitHub did not accept the draft save.");
        }

        logger.LogInformation("User {UserId} saved workflow draft {Draft} at {Commit}", actorId, id, committed);
        return candidate.View(draft with
        {
            HeadSha = committed,
            PullRequest = draft.PullRequest is { } pr ? pr with { HeadSha = committed } : null
        }, true);

        async Task<WorkflowDraftWorkspace> RecoverSave(WorkflowDraft latest)
        {
            // A lost response can leave the complete intended edit at the head.
            if (Regex.IsMatch(input.ExpectedHeadSha, @"\A[0-9a-f]{40}\z") &&
                await git.IsDirectChild(latest.HeadSha, input.ExpectedHeadSha, ct))
            {
                var original = await ReadWorkspace(input.ExpectedHeadSha, project, ct);
                var (_, intended) = Apply(original, input);
                var saved = await ReadWorkspace(latest.HeadSha, project, ct);
                if (intended.Count > 0 && saved.Files.Count == original.Files.Count &&
                    original.Files.All(file => saved.Files.TryGetValue(file.Key, out var content) &&
                                               content == intended.GetValueOrDefault(file.Key, file.Value)))
                    return saved.View(latest, true);
            }

            throw HeadChanged();
        }
    }

    public async Task<FormDto> Preview(string projectId, string id, WorkflowAuthoringProject project,
        string form, DraftWorkspaceInput input, CancellationToken ct)
        => (await PreviewWorkspace(projectId, id, project, input, ct)).Preview(form);

    public async Task<WorkflowPreview> PreviewWorkflow(string projectId, string id, WorkflowAuthoringProject project,
        DraftWorkspaceInput input, string actorId, CancellationToken ct)
    {
        var workspace = await PreviewWorkspace(projectId, id, project, input, ct);
        var version = WorkflowPreviewSnapshot.Prefix + Guid.NewGuid().ToString("N");
        cache.Set(version, new WorkflowPreviewSnapshot(workspace.Parser, projectId, actorId), TimeSpan.FromMinutes(15));
        return new WorkflowPreview(version, workspace.Definition.Name,
            workspace.Definition.Screens.FirstOrDefault()?.Name);
    }

    private async Task<DraftWorkspace> PreviewWorkspace(string projectId, string id, WorkflowAuthoringProject project,
        DraftWorkspaceInput input, CancellationToken ct)
    {
        var draft = await Get(projectId, id, ct);
        EnsureHead(draft, input.ExpectedHeadSha);
        if (input.Files.Count > 0)
            EnsureOpen(draft);
        var workspace = await ReadWorkspace(draft.HeadSha, project, ct);
        return Apply(workspace, input).Workspace;
    }

    private static (DraftWorkspace Workspace, Dictionary<string, string> Changes) Apply(
        DraftWorkspace workspace, DraftWorkspaceInput input)
        => Edit(() => workspace.Apply(input.Files, input.YamlFiles));

    public async Task<WorkflowDraft> Propose(string projectId, string id, WorkflowAuthoringProject project,
        string expectedHead, string actorId, CancellationToken ct)
    {
        var branch = Branch(projectId, id);
        var draft = await Load(branch, id, ct);
        EnsureHead(draft, expectedHead);
        if (draft.PullRequest is { } existing)
        {
            if (existing.HeadSha != expectedHead)
                throw HeadChanged();
            return draft;
        }

        await Validate(draft, project, ct);
        if (await git.GetHead(branch, ct) != expectedHead)
            throw HeadChanged();
        var pr = await git.Propose(branch, project.Title, ct);
        if (pr.HeadSha != expectedHead)
            throw HeadChanged();

        logger.LogInformation("User {UserId} proposed workflow draft {Draft} as PR {PullRequest}", actorId, id,
            pr.Number);
        return draft with { PullRequest = pr };
    }

    public async Task<WorkflowDraft> Merge(string projectId, string id, WorkflowAuthoringProject project,
        string expectedHead, string actorId, CancellationToken ct)
    {
        var branch = Branch(projectId, id);
        var draft = await Load(branch, id, ct);
        EnsureHead(draft, expectedHead);
        if (draft.PullRequest is { State: "merged" })
            return draft;

        if (draft.PullRequest is not { State: "open" } pr)
            throw Conflict("DraftNotProposed", "This draft needs an open pull request.");
        if (pr.HeadSha != expectedHead)
            throw HeadChanged();

        await Validate(draft, project, ct);
        var mergedSha = await git.Merge(pr.Number, expectedHead, ct);
        if (mergedSha is null)
        {
            var latest = await git.FindPullRequest(branch, ct);
            if (latest is { State: "merged" } && latest.HeadSha == expectedHead)
                return draft with { PullRequest = latest };
            if (latest?.HeadSha != expectedHead)
                throw HeadChanged();
            throw Conflict("MergeBlocked",
                "GitHub blocked the merge. Check the pull request's review, checks, and merge status.");
        }

        logger.LogInformation("User {UserId} merged workflow draft {Draft} at {Commit}", actorId, id, mergedSha);
        return draft with { PullRequest = pr with { MergedSha = mergedSha, State = "merged" } };
    }

    public async Task<bool> Discard(string projectId, string id, string expectedHead, int? pullRequestNumber,
        string actorId, CancellationToken ct)
    {
        var branch = Branch(projectId, id);
        var pr = await git.FindPullRequest(branch, ct);
        if (pr is { State: "merged" })
            throw Conflict("DraftMerged", "This draft has already been merged.");

        var head = await git.GetHead(branch, ct);
        if (head is null && pr?.State != "open")
            return true;
        if (head != expectedHead)
            throw HeadChanged();
        if (pr?.Number != pullRequestNumber)
            throw Conflict("DraftProposalChanged", "The pull request changed. Reload the draft before discarding it.");

        if (pr is { State: "open" })
        {
            if (pr.HeadSha != expectedHead)
                throw HeadChanged();
            var closed = await git.Close(pr.Number, ct);
            if (closed.State != "closed")
                throw Conflict("DraftProposalChanged", "The pull request could not be closed. Reload the draft.");
        }

        if (!await git.TryDeleteBranch(branch, expectedHead, ct))
            throw HeadChanged();

        logger.LogInformation("User {UserId} discarded workflow draft {Draft}", actorId, id);
        return true;
    }

    private async Task<WorkflowDraft> Load(string branch, string id, CancellationToken ct)
    {
        var headTask = git.GetHead(branch, ct);
        var prTask = git.FindPullRequest(branch, ct);
        await Task.WhenAll(headTask, prTask);
        var pr = await prTask;
        var branchHead = await headTask;
        var head = pr is { State: "merged" }
            ? pr.HeadSha
            : branchHead ?? (pr is { State: "closed" } ? pr.HeadSha : null);
        if (head is null)
            throw new WorkflowAuthoringException(404, "DraftNotFound", "The workflow draft was not found.");

        var baseSha = id[..40];
        return new WorkflowDraft(id, baseSha, git.GetCommitUrl(baseSha), head, pr, branchHead is not null);
    }

    private async Task Validate(WorkflowDraft draft, WorkflowAuthoringProject project, CancellationToken ct)
    {
        var comparison = await git.Compare(draft.HeadSha, ct);
        if (comparison.Files.Length == 0)
            throw Conflict("EmptyDraft", "Save a change before proposing this draft.");

        // GitHub caps compare results at 300 files; a full result might omit out-of-scope changes.
        if (comparison.Files.Length >= 300 || comparison.Files.Any(file => file.Status != "modified" ||
                                                                           !file.Path.StartsWith(project.Folder + "/",
                                                                               StringComparison.Ordinal)))
            throw InvalidChanges();

        var original = await ReadWorkspace(comparison.BaseSha, project, ct);
        var candidate = await ReadWorkspace(draft.HeadSha, project, ct);
        var changedPaths = comparison.Files.Select(file => file.Path).ToArray();
        Check(original, candidate);

        if (comparison.TargetSha != comparison.BaseSha)
        {
            var target = await ReadWorkspace(comparison.TargetSha, project, ct);
            if (changedPaths.Any(path =>
                    original.Files.GetValueOrDefault(path) != target.Files.GetValueOrDefault(path)))
                throw Conflict("DraftBaseChanged",
                    "The target branch changed a file in this draft. Review the changes in GitHub.");
            var changes =
                changedPaths.ToDictionary(path => path, path => candidate.Files[path], StringComparer.Ordinal);
            var merged = Edit(() => target.WithFiles(changes));
            Check(target, merged);
        }

        void Check(DraftWorkspace baseline, DraftWorkspace proposed) => Edit(() =>
        {
            baseline.CheckChanges(proposed, changedPaths);
            return true;
        });
    }

    private Task<DraftWorkspace> ReadWorkspace(string sha, WorkflowAuthoringProject project, CancellationToken ct)
        => ValidateConfigurationAsync(async () => new DraftWorkspace(await git.ReadRevisionFiles(sha, ct), project),
            sha);

    private async Task<T> ValidateConfigurationAsync<T>(Func<Task<T>> action, string sha)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not (WorkflowAuthoringException or HttpRequestException
                                       or OperationCanceledException))
        {
            throw InvalidConfiguration(ex, sha);
        }
    }

    private WorkflowAuthoringException InvalidConfiguration(Exception ex, string sha)
    {
        logger.LogWarning(ex, "Workflow draft config did not validate at {Commit}", sha);
        return new WorkflowAuthoringException(422, "InvalidDraftConfiguration", "The draft configuration is invalid.");
    }

    private static T Edit<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not (WorkflowAuthoringException or OperationCanceledException))
        {
            throw new WorkflowAuthoringException(422, "InvalidDraftConfiguration",
                "The draft changes do not produce a valid workflow configuration.");
        }
    }

    private static void EnsureOpen(WorkflowDraft draft)
    {
        if (draft.PullRequest is { State: not "open" })
            throw Conflict("DraftClosed", "This workflow draft is closed.");
    }

    private static void EnsureHead(WorkflowDraft draft, string expectedHead)
    {
        if (draft.HeadSha != expectedHead)
            throw HeadChanged();
    }

    private static WorkflowAuthoringException HeadChanged()
    {
        return Conflict("DraftHeadChanged", "The draft changed. Reload the saved revision before continuing.");
    }

    private static WorkflowAuthoringException InvalidChanges()
    {
        return new WorkflowAuthoringException(403, "DraftChangesForbidden",
            "This draft contains changes outside the permitted form content.");
    }

    private static WorkflowAuthoringException Conflict(string code, string message)
    {
        return new WorkflowAuthoringException(409, code, message);
    }
}