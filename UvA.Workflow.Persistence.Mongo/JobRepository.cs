namespace UvA.Workflow.Persistence.Mongo;

public class JobRepository(IMongoDatabase database, IOptions<WorkerOptions> workerOptions) : IJobRepository
{
    private readonly IMongoCollection<Job> _jobCollection =
        database.GetCollection<Job>("jobs");

    public Task Add(Job job, CancellationToken ct)
        => _jobCollection.InsertOneAsync(job, cancellationToken: ct);

    public async Task<Job?> GetById(string instanceId, string jobId, CancellationToken ct)
    {
        var filter = Builders<Job>.Filter.And(
            Builders<Job>.Filter.Eq(j => j.InstanceId, instanceId),
            Builders<Job>.Filter.Eq(j => j.Id, jobId));

        return await _jobCollection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Job>> GetList(string instanceId, CancellationToken ct)
    {
        var filter = Builders<Job>.Filter.Eq(j => j.InstanceId, instanceId);
        return await _jobCollection.Find(filter)
            .SortByDescending(j => j.StartOn)
            .ToListAsync(ct);
    }

    public async Task<Job?> TryClaimJob(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var f = Builders<Job>.Filter;

        var correctGroup = f.Eq(j => j.WorkerGroup, workerOptions.Value.WorkerGroup);
        var hasStarted = f.Lte(j => j.StartOn, now);

        var pending = f.Eq(j => j.Status, JobStatus.Pending);
        var expiredRunning = f.And(
            f.Eq(j => j.Status, JobStatus.Running),
            f.Lt(j => j.ClaimedUntil, now)
        );

        var claimable = f.Or(pending, expiredRunning);

        var filter = f.And(correctGroup, hasStarted, claimable);

        var update = Builders<Job>.Update.Set(j => j.Status, JobStatus.Running)
            .Set(j => j.ClaimedUntil, now.Add(workerOptions.Value.JobClaimDuration));

        return await _jobCollection.FindOneAndUpdateAsync(filter, update,
            new FindOneAndUpdateOptions<Job>
            {
                ReturnDocument = ReturnDocument.After,
                Sort = Builders<Job>.Sort.Ascending(j => j.StartOn)
            }, ct);
    }

    public Task Update(Job job, CancellationToken ct)
    {
        var jobId = ObjectId.Parse(job.Id);
        return _jobCollection.ReplaceOneAsync(Builders<Job>.Filter.Eq("_id", jobId), job, cancellationToken: ct);
    }
}