using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UvA.Workflow.Jobs;

public class JobWorker(IServiceProvider serviceProvider, ILogger<JobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // TODO: is this safe or should it not be long-lived?
        var jobRepository = serviceProvider.CreateScope().ServiceProvider.GetRequiredService<IJobRepository>();
        while (!ct.IsCancellationRequested)
        {
            var jobs = await jobRepository.GetPendingJobs(ct);
            var jobService = serviceProvider.CreateScope().ServiceProvider.GetRequiredService<JobService>();
            foreach (var job in jobs)
            {
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

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}