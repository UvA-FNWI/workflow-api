using System.Text.RegularExpressions;

namespace UvA.Workflow.Api.WorkflowAuthoring;

public sealed class WorkflowAuthoringOptions
{
    public bool Enabled { get; set; }
    public string RepoUrl { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public string GitHubAppId { get; set; } = "";
    public long GitHubInstallationId { get; set; }
    public string GitHubPrivateKey { get; set; } = "";
    public Dictionary<string, WorkflowAuthoringProject> Projects { get; set; } = [];
    public Dictionary<string, WorkflowAuthoringGrant> Grants { get; set; } = [];

    public bool IsConfigured()
        => RepositoryName(RepoUrl) is not null &&
           Regex.IsMatch(TargetBranch, @"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}\z") &&
           !string.IsNullOrWhiteSpace(GitHubAppId) && GitHubInstallationId > 0 &&
           !string.IsNullOrWhiteSpace(GitHubPrivateKey) && Projects.Count > 0 &&
           Projects.All(entry =>
               Regex.IsMatch(entry.Key, @"\A[a-z0-9][a-z0-9-]{0,59}\z") &&
               IsFolder(entry.Value.Folder) && !string.IsNullOrWhiteSpace(entry.Value.Title)) &&
           Grants.Keys.All(IsFolder);

    internal (bool CanEdit, bool CanMerge) Permissions(string folder, string userId)
    {
        var grants = Grants.Where(entry => folder == entry.Key ||
                                           folder.StartsWith(entry.Key + "/", StringComparison.Ordinal))
            .Select(entry => entry.Value).ToArray();
        return (grants.Any(grant => grant.EditorUserIds.Contains(userId, StringComparer.Ordinal)),
            grants.Any(grant => grant.MaintainerUserIds.Contains(userId, StringComparer.Ordinal)));
    }

    private static bool IsFolder(string path) => !path.Split('/').Contains("Common") && path.Split('/').All(part =>
        Regex.IsMatch(part, @"\A[a-zA-Z0-9][a-zA-Z0-9_.-]*\z"));

    internal static string? RepositoryName(string? url)
    {
        if (url is null ||
            !Regex.IsMatch(url, @"\Ahttps://github\.com/[a-zA-Z0-9_-]+/[a-zA-Z0-9_.-]+/?\z"))
            return null;

        var name = url["https://github.com/".Length..].TrimEnd('/');
        return name.EndsWith(".git", StringComparison.Ordinal) ? name[..^4] : name;
    }
}

public sealed class WorkflowAuthoringProject
{
    public string Title { get; set; } = "";
    public string Folder { get; set; } = "";
}

public sealed class WorkflowAuthoringGrant
{
    public string[] EditorUserIds { get; set; } = [];
    public string[] MaintainerUserIds { get; set; } = [];
}