using System.Threading.Channels;

namespace UvA.Workflow.Api.Infrastructure;

/// Reloads a local checkout after saves, keeping the installed model when parsing fails.
public class WorkflowConfigWatcher(
    WorkflowConfigLoader loader,
    IOptions<WorkflowSourceOptions> options,
    ILogger<WorkflowConfigWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WatchLocalChanges || string.IsNullOrWhiteSpace(options.Value.LocalPath))
            return;

        var path = Path.GetFullPath(options.Value.LocalPath);
        // File events may arrive in bursts. One pending signal is enough to reload the whole config.
        var changes = Channel.CreateBounded<bool>(1);
        using var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };

        void OnChange(object sender, FileSystemEventArgs args)
        {
            if (args.Name?.Split(Path.DirectorySeparatorChar).Contains(".git") != true)
                changes.Writer.TryWrite(true);
        }

        watcher.Changed += OnChange;
        watcher.Created += OnChange;
        watcher.Deleted += OnChange;
        watcher.Renamed += OnChange;
        watcher.Error += (_, args) =>
        {
            logger.LogWarning(args.GetException(), "Local config watcher error; requesting a full reload");
            changes.Writer.TryWrite(true);
        };
        watcher.EnableRaisingEvents = true;
        logger.LogInformation("Watching local workflow config at {LocalPath}", path);
        // Pick up any edits made between the startup load and enabling the watcher.
        changes.Writer.TryWrite(true);

        while (await changes.Reader.WaitToReadAsync(stoppingToken))
        {
            // Wait until saves have settled before parsing (including editors that replace files).
            do
            {
                while (changes.Reader.TryRead(out _))
                {
                }

                await Task.Delay(500, stoppingToken);
            } while (changes.Reader.TryPeek(out _));

            try
            {
                await loader.LoadBaselineAsync();
                logger.LogInformation("Reloaded local workflow config");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The loader only installs a model after parsing succeeds. The next save retries.
                logger.LogWarning(ex, "Local config reload failed; keeping the last working config");
            }
        }
    }
}