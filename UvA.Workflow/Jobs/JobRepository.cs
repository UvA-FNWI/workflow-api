namespace UvA.Workflow.Jobs;

public interface IJobRepository
{
    Task Add(Job job, CancellationToken ct);
    Task<IEnumerable<Job>> GetPendingJobs(CancellationToken ct);
    Task Update(Job job, CancellationToken ct);
}