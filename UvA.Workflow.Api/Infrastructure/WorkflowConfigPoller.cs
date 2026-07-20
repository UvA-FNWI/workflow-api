namespace UvA.Workflow.Api.Infrastructure;

/// Periodically checks for a new baseline commit. Each pod updates its own in-memory config.
public class WorkflowConfigPoller(
    WorkflowConfigLoader loader,
    IOptions<WorkflowSourceOptions> options,
    ILogger<WorkflowConfigPoller> logger) : BackgroundService
{
    private readonly WorkflowSourceOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_opts.PollIntervalSeconds <= 0 || string.IsNullOrWhiteSpace(_opts.RepoUrl))
        {
            logger.LogDebug("Config polling disabled (no RepoUrl, or PollIntervalSeconds <= 0)");
            return;
        }

        if (!loader.CanPoll)
        {
            logger.LogWarning("Config polling disabled because no baseline was loaded from the repo");
            return;
        }

        var interval = TimeSpan.FromSeconds(_opts.PollIntervalSeconds);
        logger.LogInformation("Config polling enabled: {RepoUrl} ref {Ref}, every {Interval}",
            _opts.RepoUrl, _opts.Ref, interval);

        // Randomize the first check so restarted pods don't all poll GitHub on the same tick.
        var delay = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * interval.TotalMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(delay, stoppingToken);
            try
            {
                // The loader logs the install when the ref actually moved; stay quiet otherwise.
                if (!await loader.ReloadBaselineIfChangedAsync())
                    logger.LogDebug("Config unchanged at ref {Ref}", _opts.Ref);
                delay = interval;
            }
            catch (WorkflowConfigFetchException ex) when (!stoppingToken.IsCancellationRequested)
            {
                delay = ex.RetryAfter ?? interval;
                logger.LogWarning(ex, "Config poll failed; retrying after {Delay}", delay);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                delay = interval;
                logger.LogWarning(ex, "Config poll failed; retrying next tick");
            }
        }
    }
}