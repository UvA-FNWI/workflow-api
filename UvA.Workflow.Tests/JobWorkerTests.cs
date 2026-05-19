using MongoDB.Bson;
using Moq;
using UvA.Workflow.Jobs;

namespace UvA.Workflow.Tests;

public class JobWorkerTests
{
    private readonly Mock<IJobRepository> _jobRepositoryMock = new();
    private readonly Mock<JobService> _jobServiceMock;
    private readonly WorkerOptions _workerOptions = new() { WorkerGroup = "test" };

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
    public async Task TryClaimJob_ReturnsNull_WhenNoJobsForWorkerGroup()
    {
        _jobRepositoryMock
            .Setup(r => r.TryClaimJob(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var result = await _jobRepositoryMock.Object.TryClaimJob(CancellationToken.None);

        Assert.Null(result);
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