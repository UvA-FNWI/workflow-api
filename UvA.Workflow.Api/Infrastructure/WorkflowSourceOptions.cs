namespace UvA.Workflow.Api.Infrastructure;

/// Where workflow definitions load from. Config section "WorkflowSource".
public class WorkflowSourceOptions
{
    /// When set, load config straight from this local directory (dev/tests); no fetch.
    public string? LocalPath { get; set; }

    /// Watch LocalPath for changes and reload automatically. Disabled by default; requires an API restart.
    public bool WatchLocalChanges { get; set; } = false;

    /// GitHub repo to fetch from.
    public string? RepoUrl { get; set; }

    /// Branch, tag, or SHA to fetch.
    public string Ref { get; set; } = "main";

    /// Optional token for a private repo.
    public string? Token { get; set; }

    /// Seconds between config checks. 0 disables polling.
    public int PollIntervalSeconds { get; set; } = 300;
}