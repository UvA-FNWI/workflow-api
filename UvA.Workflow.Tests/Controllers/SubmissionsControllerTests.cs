using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Impersonation;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class SubmissionsControllerTests : ControllerTestsBase
{
    private readonly SubmissionService _submissionService;
    private readonly SubmissionDtoFactory _submissionDtoFactory;

    public SubmissionsControllerTests() : base()
    {
        _submissionService =
            new SubmissionService(_instanceRepoMock.Object, _modelService, _instanceService,
                _instanceJournalServiceMock.Object, _workflowInstanceService, _jobService, _effectService);
        _submissionDtoFactory =
            new SubmissionDtoFactory(new ArtifactTokenService(_configurationMock.Object), _modelService);
    }

    [Theory]
    [InlineData("Coordinator")]
    [InlineData("Student")]
    public async Task Submissions_GetSubmission_OnlyAllowWithViewRights(string role)
    {
        // Arrange
        const string submissionId = "Start";
        var (controller, instance) = BuildControllerWithRoles([role], submissionId);
        // Act
        var result = await controller.GetSubmission(instance.Id, submissionId, null, _ct);

        //Assert
        Assert.IsType<ActionResult<SubmissionDto>>(result);
    }

    [Theory]
    [InlineData("Student")]
    public async Task Submissions_SubmitSubmission_OnlyAllowWithSubmitRights(string role)
    {
        // Arrange
        const string submissionId = "Start";
        var (controller, instance) = BuildControllerWithRoles([role], submissionId);

        // Act
        var result = await controller.SubmitSubmission(instance.Id, submissionId, _ct);

        //Assert
        Assert.IsType<ActionResult<SubmitSubmissionResult>>(result);
    }

    [Theory]
    [InlineData("Coordinator")]
    public async Task Submissions_SubmitSubmission_ThrowsForbiddenException(string role)
    {
        // Arrange
        const string submissionId = "Start";
        var (controller, instance) = BuildControllerWithRoles([role], submissionId);

        // Act and Assert
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            controller.SubmitSubmission(instance.Id, submissionId, _ct));
    }

    private (SubmissionsController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string submissionId)
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Upload")
            .WithEvents(b => b.WithId(submissionId))
            .Build();

        _instanceRepoMock.Setup(r => r.GetById(instance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _userServiceMock.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);

        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ControllerTestsHelpers.AdminUser);

        var controller = new SubmissionsController(_userServiceMock.Object, _modelService, _rightsService,
            _submissionService, _submissionDtoFactory, null);

        return (controller, instance);
    }
}