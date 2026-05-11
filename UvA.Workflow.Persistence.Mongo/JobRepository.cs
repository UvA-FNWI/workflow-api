namespace UvA.Workflow.Persistence.Mongo;

public class JobRepository(IMongoDatabase database) : IJobRepository
{
    private readonly IMongoCollection<Job> _jobCollection =
        database.GetCollection<Job>("jobs");

    public Task Add(Job job, CancellationToken ct)
        => _jobCollection.InsertOneAsync(job, cancellationToken: ct);

    public async Task<IEnumerable<Job>> GetPendingJobs(CancellationToken ct)
    {
        var result = await _jobCollection.FindAsync(
            j => j.Status == JobStatus.Pending && j.StartOn < DateTime.Now,
            cancellationToken: ct
        );
        return await result.ToListAsync(ct);
    }

    public Task Update(Job job, CancellationToken ct)
    {
        var jobId = ObjectId.Parse(job.Id);
        return _jobCollection.ReplaceOneAsync(Builders<Job>.Filter.Eq("_id", jobId), job, cancellationToken: ct);
    }
}