using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UvA.Workflow.Jobs;

public class JobWorker(
    IServiceProvider serviceProvider,
    ILogger<JobWorker> logger,
    IOptions<WorkerOptions> workerOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // TODO: is this safe or should it not be long-lived?
        var jobRepository = serviceProvider.CreateScope().ServiceProvider.GetRequiredService<IJobRepository>();

        while (!ct.IsCancellationRequested)
        {
            var job = await jobRepository.TryClaimJob(ct);

            if (job == null)
            {
                await Task.Delay(workerOptions.Value.JobPollingInterval, ct);
                continue;
            }

            var jobService = serviceProvider.CreateScope().ServiceProvider.GetRequiredService<JobService>();

            try
            {
                await jobService.RunJob(job, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running job {JobId}", job.Id);
                job.Message = ex.ToString();
                job.Status = JobStatus.Failed;
                await jobRepository.Update(job, ct);
            }
        }
    }
}