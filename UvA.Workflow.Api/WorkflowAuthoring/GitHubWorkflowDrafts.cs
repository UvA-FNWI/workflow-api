using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;

namespace UvA.Workflow.Api.WorkflowAuthoring;

public sealed record DraftPullRequest(int Number, string Url, string State, string HeadSha, string? MergedSha);

public sealed record DraftComparison(string BaseSha, string TargetSha, DraftChangedFile[] Files);

public sealed record DraftChangedFile(string Path, string Status);

public sealed class GitHubWorkflowDrafts(
    IHttpClientFactory clients,
    GitHubAppToken token,
    IOptions<WorkflowAuthoringOptions> options)
{
    private string Repository => WorkflowAuthoringOptions.RepositoryName(options.Value.RepoUrl)!;
    private string RepoPath => "repos/" + Repository;
    private string Target => options.Value.TargetBranch;

    internal string GetCommitUrl(string sha)
    {
        return $"https://github.com/{Repository}/commit/{sha}";
    }

    public async Task<string?> GetHead(string branch, CancellationToken ct)
    {
        var result = await Send(HttpMethod.Get, $"{RepoPath}/git/ref/heads/{EscapePath(branch)}", ct,
            allowed: [HttpStatusCode.NotFound]);
        return result?.GetProperty("object").GetProperty("sha").GetString();
    }

    public async Task<string> GetBase(CancellationToken ct)
    {
        return await GetHead(Target, ct) ?? throw new HttpRequestException("The target branch is unavailable.");
    }

    public async Task<AuthoringProjectDrafts> List(string branchPrefix, CancellationToken ct)
    {
        const string query = """
                             query Drafts($owner: String!, $name: String!, $target: String!,
                                          $qualifiedTarget: String!, $prefix: String!, $cursor: String) {
                               repository(owner: $owner, name: $name) {
                                 target: ref(qualifiedName: $qualifiedTarget) { target { oid } }
                                 refs(refPrefix: "refs/heads/", query: $prefix, first: 50, after: $cursor) {
                                   nodes {
                                     name
                                     target { oid ... on Commit { committedDate } }
                                     associatedPullRequests(baseRefName: $target, last: 1) {
                                       nodes { state updatedAt headRefName headRepository { nameWithOwner } }
                                     }
                                   }
                                   pageInfo { hasNextPage endCursor }
                                 }
                               }
                             }
                             """;
        var repository = Repository.Split('/');
        string? cursor = null;
        string? baseSha = null;
        var drafts = new List<AuthoringDraftSummary>();
        do
        {
            var response = await Send(HttpMethod.Post, "graphql", ct, new
            {
                query,
                variables = new
                {
                    owner = repository[0], name = repository[1], target = Target,
                    qualifiedTarget = "refs/heads/" + Target,
                    prefix = branchPrefix, cursor
                }
            });
            if (response is not { } data ||
                data.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0 ||
                !data.TryGetProperty("data", out var result) || result.ValueKind != JsonValueKind.Object ||
                result.GetProperty("repository") is not { ValueKind: JsonValueKind.Object } repo ||
                repo.GetProperty("target") is not { ValueKind: JsonValueKind.Object } target)
                throw new HttpRequestException("GitHub did not return the draft list.");

            baseSha ??= target.GetProperty("target").GetProperty("oid").GetString();
            var refs = repo.GetProperty("refs");
            foreach (var item in refs.GetProperty("nodes").EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    item.GetProperty("name").GetString() is not { } branch ||
                    !branch.StartsWith(branchPrefix, StringComparison.Ordinal))
                    continue;

                var id = branch[branchPrefix.Length..];
                if (!WorkflowDraftService.IsDraftId(id))
                    continue;

                var commit = item.GetProperty("target");
                var hasSavedChanges = commit.GetProperty("oid").GetString() != id[..40];
                DateTimeOffset? updatedAt = hasSavedChanges && commit.TryGetProperty("committedDate", out var date)
                    ? date.GetDateTimeOffset()
                    : null;
                AuthoringDraftReview? review = null;
                foreach (var pr in item.GetProperty("associatedPullRequests").GetProperty("nodes").EnumerateArray())
                {
                    if (pr.ValueKind != JsonValueKind.Object ||
                        pr.GetProperty("headRefName").GetString() != branch ||
                        pr.GetProperty("headRepository") is not { ValueKind: JsonValueKind.Object } origin ||
                        !string.Equals(origin.GetProperty("nameWithOwner").GetString(), Repository,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    review = new AuthoringDraftReview(pr.GetProperty("state").GetString()!.ToLowerInvariant());
                    var prUpdatedAt = pr.GetProperty("updatedAt").GetDateTimeOffset();
                    if (updatedAt is null || prUpdatedAt > updatedAt)
                        updatedAt = prUpdatedAt;
                    break;
                }

                drafts.Add(new AuthoringDraftSummary(id, hasSavedChanges, updatedAt, review));
            }

            var page = refs.GetProperty("pageInfo");
            cursor = page.GetProperty("hasNextPage").GetBoolean()
                ? page.GetProperty("endCursor").GetString() ??
                  throw new HttpRequestException("GitHub did not return the next page of drafts.")
                : null;
        } while (cursor is not null);

        var sorted = drafts
            .OrderBy(draft => draft.PullRequest?.State is "closed" or "merged")
            .ThenByDescending(draft => draft.UpdatedAt)
            .ThenBy(draft => draft.Id, StringComparer.Ordinal)
            .ToArray();
        return new AuthoringProjectDrafts(
            baseSha ?? throw new HttpRequestException("The target branch is unavailable."),
            sorted);
    }

    public async Task Create(string branch, string sha, CancellationToken ct)
    {
        var result = await Send(HttpMethod.Post, $"{RepoPath}/git/refs", ct,
            new { @ref = $"refs/heads/{branch}", sha }, [HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict]);
        if (result is null && await GetHead(branch, ct) is null)
            throw new HttpRequestException("GitHub did not create the draft branch.");
    }

    public async Task<Dictionary<string, string>> ReadRevisionFiles(string sha, CancellationToken ct)
    {
        using var client = clients.CreateClient(GitHubAppToken.ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RepoPath}/tarball/{Uri.EscapeDataString(sha)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token.Get(ct));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var files = await WorkflowConfigLoader.ReadTarballAsync(stream, ct);
        files.Remove(WorkflowConfigLoader.LayoutPath);
        return files;
    }

    public async Task<bool> IsDirectChild(string head, string parent, CancellationToken ct)
    {
        var result = await Send(HttpMethod.Get, $"{RepoPath}/git/commits/{head}", ct);
        var parents = result!.Value.GetProperty("parents");
        return parents.GetArrayLength() == 1 && parents[0].GetProperty("sha").GetString() == parent;
    }

    public async Task<string?> Save(string branch, string head, IReadOnlyDictionary<string, string> files,
        CancellationToken ct)
    {
        const string query = """
                             mutation Save($input: CreateCommitOnBranchInput!) {
                               createCommitOnBranch(input: $input) { commit { oid } }
                             }
                             """;
        var input = new
        {
            branch = new { repositoryNameWithOwner = Repository, branchName = branch },
            expectedHeadOid = head,
            message = new { headline = "Edit workflow form" },
            fileChanges = new
            {
                additions = files.Select(file => new
                {
                    path = file.Key, contents = Convert.ToBase64String(Encoding.UTF8.GetBytes(file.Value))
                }).ToArray()
            }
        };
        var result = await Send(HttpMethod.Post, "graphql", ct, new { query, variables = new { input } });
        var body = result!.Value;
        if (body.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            return null;
        return body.GetProperty("data").GetProperty("createCommitOnBranch").GetProperty("commit")
            .GetProperty("oid").GetString();
    }

    public async Task<DraftComparison> Compare(string head, CancellationToken ct)
    {
        var target = await GetBase(ct);
        var result = await Send(HttpMethod.Get, $"{RepoPath}/compare/{target}...{head}", ct);
        var comparison = result!.Value;
        return new DraftComparison(comparison.GetProperty("merge_base_commit").GetProperty("sha").GetString()!, target,
            comparison.GetProperty("files").EnumerateArray()
                .Select(file => new DraftChangedFile(file.GetProperty("filename").GetString()!,
                    file.GetProperty("status").GetString()!)).ToArray());
    }

    public async Task<DraftPullRequest?> FindPullRequest(string branch, CancellationToken ct)
    {
        var owner = Repository.Split('/')[0];
        var head = Uri.EscapeDataString($"{owner}:{branch}");
        var result = await Send(HttpMethod.Get, $"{RepoPath}/pulls?state=all&base={Target}&head={head}", ct);
        return result!.Value.EnumerateArray()
            .Where(pr => pr.GetProperty("head").GetProperty("ref").GetString() == branch &&
                         pr.GetProperty("head").GetProperty("repo").ValueKind == JsonValueKind.Object &&
                         string.Equals(pr.GetProperty("head").GetProperty("repo").GetProperty("full_name").GetString(),
                             Repository, StringComparison.OrdinalIgnoreCase))
            .Select(ReadPullRequest).FirstOrDefault();
    }

    public async Task<DraftPullRequest> Propose(string branch, string projectTitle, CancellationToken ct)
    {
        var result = await Send(HttpMethod.Post, $"{RepoPath}/pulls", ct, new
        {
            title = $"Edit workflow form: {projectTitle}",
            body = "Proposed with the workflow builder.",
            head = branch,
            @base = Target
        }, [HttpStatusCode.UnprocessableEntity]);
        if (result is { } pr)
            return ReadPullRequest(pr);

        return await FindPullRequest(branch, ct)
               ?? throw new WorkflowAuthoringException(409, "CannotPropose",
                   "GitHub could not open a pull request for this draft.");
    }

    public async Task<string?> Merge(int number, string head, CancellationToken ct)
    {
        var result = await Send(HttpMethod.Put, $"{RepoPath}/pulls/{number}/merge", ct,
            new { sha = head, merge_method = "squash" },
            [HttpStatusCode.MethodNotAllowed, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity]);
        return result is { } body && body.GetProperty("merged").GetBoolean()
            ? body.GetProperty("sha").GetString()
            : null;
    }

    public async Task<DraftPullRequest> Close(int number, CancellationToken ct)
    {
        var result = await Send(HttpMethod.Patch, $"{RepoPath}/pulls/{number}", ct, new { state = "closed" });
        return ReadPullRequest(result!.Value);
    }

    public async Task<bool> TryDeleteBranch(string branch, string expectedHead, CancellationToken ct)
    {
        const string query = """
                             mutation DeleteDraftBranch($input: UpdateRefsInput!) {
                               updateRefs(input: $input) { clientMutationId }
                             }
                             """;
        var repository = await Send(HttpMethod.Get, RepoPath, ct);
        var input = new
        {
            repositoryId = repository!.Value.GetProperty("node_id").GetString(),
            refUpdates = new[]
            {
                new { name = $"refs/heads/{branch}", beforeOid = expectedHead, afterOid = new string('0', 40) }
            }
        };
        var result = await Send(HttpMethod.Post, "graphql", ct, new { query, variables = new { input } });
        if (result is { } body && body.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object && data.TryGetProperty("updateRefs", out var update) &&
            update.ValueKind == JsonValueKind.Object)
            return true;

        // GitHub may reject deletion because the branch changed or already disappeared.
        var currentHead = await GetHead(branch, ct);
        if (currentHead == expectedHead)
            throw new HttpRequestException("GitHub did not confirm the draft branch deletion.");
        return currentHead is null;
    }

    private static DraftPullRequest ReadPullRequest(JsonElement pr)
    {
        var merged = pr.GetProperty("merged_at").ValueKind != JsonValueKind.Null;
        return new DraftPullRequest(pr.GetProperty("number").GetInt32(), pr.GetProperty("html_url").GetString()!,
            merged ? "merged" : pr.GetProperty("state").GetString()!,
            pr.GetProperty("head").GetProperty("sha").GetString()!,
            merged ? pr.GetProperty("merge_commit_sha").GetString() : null);
    }

    private async Task<JsonElement?> Send(HttpMethod method, string path,
        CancellationToken ct, object? body = null, HttpStatusCode[]? allowed = null)
    {
        using var client = clients.CreateClient(GitHubAppToken.ClientName);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token.Get(ct));
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || allowed?.Contains(response.StatusCode) == true)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static string EscapePath(string path)
    {
        return string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }
}