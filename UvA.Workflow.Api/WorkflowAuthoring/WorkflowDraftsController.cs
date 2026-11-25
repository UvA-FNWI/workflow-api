using UvA.Workflow.Api.Submissions.Dtos;
using System.ComponentModel.DataAnnotations;
using UvA.Workflow.Api.Authentication;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.WorkflowInstances;

namespace UvA.Workflow.Api.WorkflowAuthoring;

public sealed record AuthoringProject(
    string Id,
    string Title,
    bool CanEdit,
    bool CanMerge);

public sealed record AuthoringProjectDrafts(string BaseSha, AuthoringDraftSummary[] Drafts);

public sealed record AuthoringDraftSummary(
    string Id,
    bool HasSavedChanges,
    DateTimeOffset? UpdatedAt,
    AuthoringDraftReview? PullRequest);

public sealed record AuthoringDraftReview(string State);

public sealed record DraftHeadInput(
    [RegularExpression(@"\A[0-9a-f]{40}\z")]
    string ExpectedHeadSha);

public sealed record DiscardDraftInput(
    [RegularExpression(@"\A[0-9a-f]{40}\z")]
    string ExpectedHeadSha,
    [Range(1, int.MaxValue)] int? PullRequestNumber);

public sealed record DraftWorkspaceInput(
    [RegularExpression(@"\A[0-9a-f]{40}\z")]
    string ExpectedHeadSha,
    [Required] Dictionary<string, string> Files,
    [Required] string[] YamlFiles);

public class WorkflowDraftsController(
    WorkflowDraftService drafts,
    GitHubWorkflowDrafts git,
    IUserService users,
    IWebHostEnvironment environment,
    IOptions<WorkflowAuthoringOptions> options) : ApiControllerBase
{
    private WorkflowAuthoringOptions Settings => options.Value;

    [HttpGet]
    public async Task<AuthoringProject[]> List(CancellationToken ct)
    {
        var user = await GetAuthor(ct);
        if (user is null)
            return [];

        return Settings.Projects.Select(entry =>
            {
                var (canEdit, canMerge) = Settings.Permissions(entry.Value.Folder, user.Id);
                return new AuthoringProject(entry.Key, entry.Value.Title, canEdit, canMerge);
            })
            .Where(project => project.CanEdit || project.CanMerge).ToArray();
    }

    [HttpGet("{project}")]
    public Task<AuthoringProjectDrafts> ListDrafts(string project, CancellationToken ct)
        => Run(project, null, false, false, (_, _) => git.List($"builder/{project}/", ct), ct);

    [HttpPut("{project}/{draft}")]
    public Task<WorkflowDraft> Open(string project, string draft, CancellationToken ct)
        => Run(project, draft, true, false, (scope, _) => drafts.Open(project, draft, scope, ct), ct);

    [HttpGet("{project}/{draft}")]
    public Task<WorkflowDraft> Get(string project, string draft, CancellationToken ct)
        => Run(project, draft, false, false, (scope, _) => drafts.Get(project, draft, ct), ct);

    [HttpGet("{project}/{draft}/Workspace")]
    public Task<WorkflowDraftWorkspace> GetWorkspace(string project, string draft, CancellationToken ct)
        => Run(project, draft, false, false, (scope, user) =>
            drafts.GetWorkspace(project, draft, scope, Settings.Permissions(scope.Folder, user).CanEdit, ct), ct);

    [HttpGet("{project}/{draft}/Changes")]
    public Task<DraftFormChange[]> GetFormChanges(string project, string draft,
        [FromQuery] string expectedHeadSha, CancellationToken ct)
        => Run(project, draft, false, false, (scope, _) =>
            drafts.GetFormChanges(project, draft, scope, expectedHeadSha, ct), ct);

    [HttpPut("{project}/{draft}/Workspace")]
    [RequestSizeLimit(256_000)]
    public Task<WorkflowDraftWorkspace> Save(string project, string draft, [FromBody] DraftWorkspaceInput input,
        CancellationToken ct)
        => Run(project, draft, true, false, (scope, user) => drafts.Save(project, draft, scope, input, user, ct), ct);

    [HttpPost("{project}/{draft}/Preview")]
    [RequestSizeLimit(256_000)]
    public Task<FormDto> Preview(string project, string draft, [FromQuery] string form,
        [FromBody] DraftWorkspaceInput input, CancellationToken ct)
        => Run(project, draft, input.Files.Count > 0, false, (scope, _) =>
            drafts.Preview(project, draft, scope, form, input, ct), ct);

    [HttpPost("{project}/{draft}/PreviewWorkflow")]
    [RequestSizeLimit(256_000)]
    public Task<WorkflowPreview> PreviewWorkflow(string project, string draft,
        [FromBody] DraftWorkspaceInput input, CancellationToken ct)
        => Run(project, draft, input.Files.Count > 0, false, (scope, user) =>
            drafts.PreviewWorkflow(project, draft, scope, input, user, ct), ct);

    [HttpPost("{project}/{draft}/PullRequest")]
    public Task<WorkflowDraft> Propose(string project, string draft, [FromBody] DraftHeadInput input,
        CancellationToken ct)
        => Run(project, draft, true, false,
            (scope, user) => drafts.Propose(project, draft, scope, input.ExpectedHeadSha, user, ct), ct);

    [HttpPost("{project}/{draft}/Merge")]
    public Task<WorkflowDraft> Merge(string project, string draft, [FromBody] DraftHeadInput input,
        CancellationToken ct)
        => Run(project, draft, false, true,
            (scope, user) => drafts.Merge(project, draft, scope, input.ExpectedHeadSha, user, ct), ct);

    [HttpPost("{project}/{draft}/Discard")]
    public Task<bool> Discard(string project, string draft, [FromBody] DiscardDraftInput input, CancellationToken ct)
        => Run(project, draft, true, false, (_, user) =>
            drafts.Discard(project, draft, input.ExpectedHeadSha, input.PullRequestNumber, user, ct), ct);

    private async Task<User?> GetAuthor(CancellationToken ct)
    {
        if (!environment.IsDevOrTest() || !Settings.Enabled || !Settings.IsConfigured() ||
            Request.Headers.ContainsKey(UserImpersonationConstants.HeaderName) ||
            Request.Headers.ContainsKey(RoleImpersonationConstants.HeaderName))
            return null;

        return await users.GetRealUser(ct);
    }

    private async Task<T> Run<T>(string projectId, string? draftId, bool edit, bool merge,
        Func<WorkflowAuthoringProject, string, Task<T>> action, CancellationToken ct)
    {
        var user = await GetAuthor(ct);
        if (user is null || !Settings.Projects.TryGetValue(projectId, out var project))
            throw ForbiddenAuthoring();

        var (canEdit, canMerge) = Settings.Permissions(project.Folder, user.Id);
        if ((edit && !canEdit) || (merge && !canMerge) || (!canEdit && !canMerge))
            throw ForbiddenAuthoring();

        if (draftId is not null && !WorkflowDraftService.IsDraftId(draftId))
            throw new WorkflowAuthoringException(404, "DraftNotFound", "The workflow draft was not found.");

        try
        {
            return await action(project, user.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException ||
                                   (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new WorkflowAuthoringException(503, "GitHubUnavailable",
                "GitHub did not confirm the operation. Retry when it is available.");
        }
    }

    private static WorkflowAuthoringException ForbiddenAuthoring()
        => new(403, "AuthoringForbidden", "You do not have permission for this workflow authoring action.");
}