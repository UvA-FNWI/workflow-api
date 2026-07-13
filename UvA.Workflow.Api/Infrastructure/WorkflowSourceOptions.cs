namespace UvA.Workflow.Api.Infrastructure;

/// Where workflow definitions load from. Config section "WorkflowSource".
public class WorkflowSourceOptions
{
    /// When set, load config straight from this local directory (dev/tests); no fetch.
    public string? LocalPath { get; set; }

    /// GitHub repo to fetch from.
    public string? RepoUrl { get; set; }

    /// Branch, tag, or SHA to fetch.
    public string Ref { get; set; } = "main";

    /// Optional token for a private repo.
    public string? Token { get; set; }
}