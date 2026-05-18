namespace UvA.Workflow.Jobs;

public class WorkerOptions
{
    /// <summary>
    /// Unique identifier for this deployment, for example "tst", "prod", "pr-11"
    /// Injected from config / env var WORKER_GROUP
    /// </summary>
    public string WorkerGroup { get; set; } = "local";

    /// <summary>
    /// The time that a worker claims a job until it can be picked up again by another worker.
    /// </summary>
    public TimeSpan JobClaimDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The interval at which the worker polls for new jobs when it is idle.
    /// </summary>
    public TimeSpan JobPollingInterval { get; set; } = TimeSpan.FromSeconds(30);
}