using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace UvA.Workflow.Jobs;

public class JobWorker(IServiceProvider serviceProvider) : BackgroundService
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
                await jobService.RunJob(job, ct);

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}