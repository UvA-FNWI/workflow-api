using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Jobs;
using UvA.Workflow.Api.Jobs.Dtos;
using UvA.Workflow.Jobs;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class JobsControllerTests : ControllerTestsBase
{
    private const string InstanceId = "507f1f77bcf86cd799439012";
    private const string JobId = "507f1f77bcf86cd799439011";

    [Theory]
    [InlineData("Api")]
    [InlineData("Coordinator")]
    public async Task Jobs_GetList_AllowedWithViewAdminRights(string role)
    {
        var job = CreateJob();
        _jobRepositoryMock.Setup(r => r.GetList(InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);
        _userRepoMock.Setup(r => r.GetByIds(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UnitTestsHelpers.AdminUser]);

        var controller = BuildController([role]);
        var result = await controller.GetList(InstanceId, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var jobs = Assert.IsAssignableFrom<IEnumerable<JobDto>>(okResult.Value).ToList();
        Assert.Single(jobs);
        Assert.Equal(job.Id, jobs[0].Id);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, jobs[0].CreatedBy);
        Assert.Equal(UnitTestsHelpers.AdminUser.DisplayName, jobs[0].CreatedByDisplayName);
    }

    [Fact]
    public async Task Jobs_GetList_BatchesUserLookupByCreatedBy()
    {
        var job = CreateJob();
        _jobRepositoryMock.Setup(r => r.GetList(InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);
        _userRepoMock.Setup(r => r.GetByIds(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([UnitTestsHelpers.AdminUser]);

        var controller = BuildController(["Api"]);
        await controller.GetList(InstanceId, _ct);

        _userRepoMock.Verify(r => r.GetByIds(
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == UnitTestsHelpers.AdminUser.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepoMock.Verify(r => r.GetById(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Jobs_GetList_ReturnsNullCreatedByDisplayName_WhenUserNotFound()
    {
        var job = CreateJob();
        _jobRepositoryMock.Setup(r => r.GetList(InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);
        _userRepoMock.Setup(r => r.GetByIds(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var controller = BuildController(["Api"]);
        var result = await controller.GetList(InstanceId, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var jobs = Assert.IsAssignableFrom<IEnumerable<JobDto>>(okResult.Value).ToList();
        Assert.Single(jobs);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, jobs[0].CreatedBy);
        Assert.Null(jobs[0].CreatedByDisplayName);
    }

    [Fact]
    public async Task Jobs_GetList_ReturnsNullCreatedByDisplayName_WhenCreatedByIsMissing()
    {
        var job = CreateJob();
        job.CreatedBy = null;
        _jobRepositoryMock.Setup(r => r.GetList(InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);

        var controller = BuildController(["Api"]);
        var result = await controller.GetList(InstanceId, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var jobs = Assert.IsAssignableFrom<IEnumerable<JobDto>>(okResult.Value).ToList();
        Assert.Single(jobs);
        Assert.Null(jobs[0].CreatedBy);
        Assert.Null(jobs[0].CreatedByDisplayName);
        _userRepoMock.Verify(r => r.GetByIds(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Jobs_GetList_ThrowsUnauthorized_WhenNoAdminRights()
    {
        var controller = BuildController(["Student"]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.GetList(InstanceId, _ct));
    }

    [Fact]
    public async Task Jobs_GetById_ReturnsJobWithResolvedDisplayName()
    {
        var job = CreateJob();
        _jobRepositoryMock.Setup(r => r.GetById(InstanceId, job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _userRepoMock.Setup(r => r.GetById(UnitTestsHelpers.AdminUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnitTestsHelpers.AdminUser);

        var controller = BuildController(["Api"]);
        var result = await controller.GetById(InstanceId, job.Id, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var jobDto = Assert.IsType<JobDto>(okResult.Value);
        Assert.Equal(job.Id, jobDto.Id);
        Assert.Equal(job.InstanceId, jobDto.InstanceId);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, jobDto.CreatedBy);
        Assert.Equal(UnitTestsHelpers.AdminUser.DisplayName, jobDto.CreatedByDisplayName);
    }

    [Fact]
    public async Task Jobs_GetById_ReturnsNullCreatedByDisplayName_WhenUserNotFound()
    {
        var job = CreateJob();
        _jobRepositoryMock.Setup(r => r.GetById(InstanceId, job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _userRepoMock.Setup(r => r.GetById(UnitTestsHelpers.AdminUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var controller = BuildController(["Api"]);
        var result = await controller.GetById(InstanceId, job.Id, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var jobDto = Assert.IsType<JobDto>(okResult.Value);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, jobDto.CreatedBy);
        Assert.Null(jobDto.CreatedByDisplayName);
    }

    [Fact]
    public async Task Jobs_GetById_ReturnsNotFound_WhenJobDoesNotExist()
    {
        _jobRepositoryMock.Setup(r => r.GetById(InstanceId, "missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var controller = BuildController(["Api"]);
        var result = await controller.GetById(InstanceId, "missing", _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task Jobs_Run_CopiesJobExecutesCopyAndReturnsUpdatedCopyWithDisplayName()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "ApprovalCoordinator", id: InstanceId)
            .Build();
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockEmptyEventLog(instance);

        _userRepoMock.Setup(r => r.GetById(UnitTestsHelpers.AdminUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnitTestsHelpers.AdminUser);

        var job = CreateJob();
        job.SourceName = "CoordinatorApproved";
        job.Steps = [];

        Job? runCopy = null;
        _jobRepositoryMock.Setup(r => r.Add(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Callback<Job, CancellationToken>((j, _) => runCopy = j)
            .Returns(Task.CompletedTask);
        _jobRepositoryMock.Setup(r => r.GetById(InstanceId, job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _jobRepositoryMock.Setup(r => r.GetById(InstanceId, It.Is<string>(id => id != job.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, CancellationToken _) => runCopy);
        _jobRepositoryMock.Setup(r => r.Update(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = BuildController(["Api"]);
        var result = await controller.Run(InstanceId, job.Id, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var jobDto = Assert.IsType<JobDto>(okResult.Value);
        Assert.NotEqual(job.Id, jobDto.Id);
        Assert.Equal(JobStatus.Completed, jobDto.Status);
        Assert.NotNull(jobDto.ExecutedOn);
        Assert.Equal(UnitTestsHelpers.AdminUser.Id, jobDto.CreatedBy);
        Assert.Equal(UnitTestsHelpers.AdminUser.DisplayName, jobDto.CreatedByDisplayName);
        _jobRepositoryMock.Verify(r => r.GetById(InstanceId, job.Id, It.IsAny<CancellationToken>()), Times.Once);
        _jobRepositoryMock.Verify(r =>
            r.Add(It.Is<Job>(j => j.Id != job.Id && j.CreatedBy == UnitTestsHelpers.AdminUser.Id),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Jobs_Run_ReturnsNotFound_WhenJobDoesNotExist()
    {
        _jobRepositoryMock.Setup(r => r.GetById(InstanceId, "missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var controller = BuildController(["Api"]);
        var result = await controller.Run(InstanceId, "missing", _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    private static Job CreateJob() => new()
    {
        Id = JobId,
        InstanceId = InstanceId,
        SourceType = JobSource.Action,
        SourceName = "CoordinatorApproved",
        StartOn = DateTime.UtcNow,
        CreatedBy = UnitTestsHelpers.AdminUser.Id,
        Status = JobStatus.Pending,
        WorkerGroup = "test",
        Steps = []
    };

    private JobsController BuildController(string[] roles)
    {
        MockCurrentUser(roles);
        return new JobsController(_jobService, _rightsService, _jobRepositoryMock.Object, _userRepoMock.Object,
            _userServiceMock.Object);
    }
}