namespace UvA.Workflow.Jobs;

public interface IJobRepository
{
    Task Add(Job job, CancellationToken ct);
}

public class JobRepository(IMongoDatabase database) : IJobRepository
{
    private readonly IMongoCollection<Job> _jobCollection =
        database.GetCollection<Job>("jobs");

    public Task Add(Job job, CancellationToken ct)
        => _jobCollection.InsertOneAsync(job, cancellationToken: ct);
}