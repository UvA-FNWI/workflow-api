namespace UvA.Workflow.Jobs;

public interface IJobRepository
{
    Task Add(Job job, CancellationToken ct);
    Task<Job?> TryClaimJob(CancellationToken ct);
    Task Update(Job job, CancellationToken ct);
}