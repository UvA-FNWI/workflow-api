using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Jobs;
using UvA.Workflow.Persistence.Mongo;

namespace UvA.Workflow.Tests;

public class JobWorkerTests
{
    private readonly Mock<IJobRepository> _jobRepositoryMock = new();

    [Fact]
    public async Task TryClaimJob_ReturnsJob_WhenPendingJobExistsForWorkerGroup()
    {
        var job = new Job
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkerGroup = "test",
            Status = JobStatus.Pending,
            StartOn = DateTime.Now.AddMinutes(-1)
        };

        _jobRepositoryMock
            .Setup(r => r.TryClaimJob(It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var result = await _jobRepositoryMock.Object.TryClaimJob(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("test", result.WorkerGroup);
    }

    [Fact]
    public async Task TryClaimJob_ExcludesCancelledJobs()
    {
        var collection = new Mock<IMongoCollection<Job>>();
        var database = new Mock<IMongoDatabase>();
        FilterDefinition<Job>? capturedFilter = null;
        collection.Setup(value => value.FindOneAndUpdateAsync(
                It.IsAny<FilterDefinition<Job>>(),
                It.IsAny<UpdateDefinition<Job>>(),
                It.IsAny<FindOneAndUpdateOptions<Job, Job>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<Job>, UpdateDefinition<Job>, FindOneAndUpdateOptions<Job, Job>,
                CancellationToken>((filter, _, _, _) => capturedFilter = filter)
            .ReturnsAsync((Job)null!);
        database.Setup(value => value.GetCollection<Job>("jobs", null)).Returns(collection.Object);
        var repository = new JobRepository(database.Object,
            Options.Create(new WorkerOptions { WorkerGroup = "test" }));

        var result = await repository.TryClaimJob(CancellationToken.None);

        Assert.Null(result);
        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var serializer = serializerRegistry.GetSerializer<Job>();
        var filter = capturedFilter!.Render(new RenderArgs<Job>(serializer, serializerRegistry)).ToString();
        Assert.Contains(nameof(JobStatus.Pending), filter);
        Assert.Contains(nameof(JobStatus.Running), filter);
        Assert.DoesNotContain(nameof(JobStatus.Cancelled), filter);
    }

    [Fact]
    public async Task TryClaimJob_ReturnsJob_WhenRunningJobHasExpiredClaim()
    {
        var job = new Job
        {
            Id = ObjectId.GenerateNewId().ToString(),
            WorkerGroup = "test",
            Status = JobStatus.Running,
            StartOn = DateTime.Now.AddMinutes(-30),
            ClaimedUntil = DateTime.Now.AddMinutes(-5) // expired
        };

        _jobRepositoryMock
            .Setup(r => r.TryClaimJob(It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var result = await _jobRepositoryMock.Object.TryClaimJob(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(JobStatus.Running, result.Status);
        Assert.True(result.ClaimedUntil < DateTime.Now);
    }
}