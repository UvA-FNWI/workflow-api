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
    public async Task CancelPendingForOperation_OnlyCancelsMatchingPendingJobs()
    {
        var collection = new Mock<IMongoCollection<Job>>();
        var database = new Mock<IMongoDatabase>();
        var instanceId = ObjectId.GenerateNewId().ToString();
        var operationId = ObjectId.GenerateNewId().ToString();
        FilterDefinition<Job>? capturedFilter = null;
        UpdateDefinition<Job>? capturedUpdate = null;
        collection.Setup(value => value.UpdateManyAsync(
                It.IsAny<FilterDefinition<Job>>(),
                It.IsAny<UpdateDefinition<Job>>(),
                It.IsAny<UpdateOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<Job>, UpdateDefinition<Job>, UpdateOptions, CancellationToken>((filter, update,
                _, _) =>
            {
                capturedFilter = filter;
                capturedUpdate = update;
            })
            .ReturnsAsync(Mock.Of<UpdateResult>());
        database.Setup(value => value.GetCollection<Job>("jobs", null)).Returns(collection.Object);
        var repository = new JobRepository(database.Object,
            Options.Create(new WorkerOptions { WorkerGroup = "test" }));

        await repository.CancelPendingForOperation(instanceId, operationId, CancellationToken.None);

        var serializerRegistry = BsonSerializer.SerializerRegistry;
        var serializer = serializerRegistry.GetSerializer<Job>();
        var renderArgs = new RenderArgs<Job>(serializer, serializerRegistry);
        var filter = capturedFilter!.Render(renderArgs);
        var update = capturedUpdate!.Render(renderArgs);
        Assert.Equal(new ObjectId(instanceId), filter["InstanceId"].AsObjectId);
        Assert.Equal(new ObjectId(operationId), filter["Operation._id"].AsObjectId);
        Assert.Equal(nameof(JobStatus.Pending), filter["Status"].AsString);
        Assert.Equal(nameof(JobStatus.Cancelled), update["$set"]["Status"].AsString);
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