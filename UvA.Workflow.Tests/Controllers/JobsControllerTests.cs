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
    [Theory]
    [InlineData("Api")]
    [InlineData("Coordinator")]
    public async Task Jobs_GetList_AllowedWithViewAdminRights(string role)
    {
        var job = CreateJob();
        _jobRepositoryMock.Setup(r => r.GetList(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([job]);

        var controller = BuildController([role]);
        var result = await controller.GetList(null, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var jobs = Assert.IsAssignableFrom<IEnumerable<JobDto>>(okResult.Value).ToList();
        Assert.Single(jobs);
        Assert.Equal(job.Id, jobs[0].Id);
    }

    [Fact]
    public async Task Jobs_GetList_ThrowsUnauthorized_WhenNoAdminRights()
    {
        var controller = BuildController(["Student"]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.GetList(null, _ct));
    }

    [Fact]
    public async Task Jobs_GetById_ReturnsJob()
    {
        var job = CreateJob();
        _jobRepositoryMock.Setup(r => r.GetById(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        var controller = BuildController(["Api"]);
        var result = await controller.GetById(job.Id, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var jobDto = Assert.IsType<JobDto>(okResult.Value);
        Assert.Equal(job.Id, jobDto.Id);
        Assert.Equal(job.InstanceId, jobDto.InstanceId);
    }

    [Fact]
    public async Task Jobs_GetById_ReturnsNotFound_WhenJobDoesNotExist()
    {
        _jobRepositoryMock.Setup(r => r.GetById("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var controller = BuildController(["Api"]);
        var result = await controller.GetById("missing", _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task Jobs_Run_ExecutesJobAndReturnsUpdatedJob()
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "ApprovalCoordinator")
            .Build();
        MockInstance(instance);
        MockEmptyRelatedInstanceLookups();
        MockEmptyEventLog(instance);

        _userRepoMock.Setup(r => r.GetById(UnitTestsHelpers.AdminUser.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnitTestsHelpers.AdminUser);

        var job = CreateJob();
        job.InstanceId = instance.Id;
        job.SourceName = "CoordinatorApproved";
        job.Steps = [];

        _jobRepositoryMock.Setup(r => r.GetById(job.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _jobRepositoryMock.Setup(r => r.Update(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = BuildController(["Api"]);
        var result = await controller.Run(job.Id, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var jobDto = Assert.IsType<JobDto>(okResult.Value);
        Assert.Equal(JobStatus.Completed, jobDto.Status);
        Assert.NotNull(jobDto.ExecutedOn);
    }

    [Fact]
    public async Task Jobs_Run_ReturnsNotFound_WhenJobDoesNotExist()
    {
        _jobRepositoryMock.Setup(r => r.GetById("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Job?)null);

        var controller = BuildController(["Api"]);
        var result = await controller.Run("missing", _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    private static Job CreateJob() => new()
    {
        Id = "507f1f77bcf86cd799439011",
        InstanceId = "507f1f77bcf86cd799439012",
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
        return new JobsController(_jobService, _rightsService);
    }
}