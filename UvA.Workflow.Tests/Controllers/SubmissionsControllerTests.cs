using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class SubmissionsControllerTests : ControllerTestsBase
{
    private readonly SubmissionService _submissionService;
    private readonly SubmissionDtoFactory _submissionDtoFactory;
    private readonly WorkflowInstanceDtoFactory _workflowInstanceDtoFactory;

    public SubmissionsControllerTests()
    {
        _submissionService =
            new SubmissionService(_workflowInstanceRepoMock.Object, _modelService, _instanceService,
                _instanceJournalServiceMock.Object, _workflowInstanceService, _jobService, _effectService);
        _submissionDtoFactory =
            new SubmissionDtoFactory(new ArtifactTokenService(_configurationMock.Object), _modelService);
        _workflowInstanceDtoFactory =
            new WorkflowInstanceDtoFactory(_instanceService, _modelService,
                _submissionDtoFactory, _workflowInstanceRepoMock.Object, _rightsService,
                new StepVersionService(_modelService, _eventRepoMock.Object), _workflowInstanceService,
                _loggerFactory.CreateLogger<WorkflowInstanceDtoFactory>());
    }

    [Theory]
    [InlineData("Coordinator")]
    [InlineData("Student")]
    public async Task Submissions_GetSubmission_AllowWithViewRights(string role)
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
    public async Task Submissions_SubmitSubmission_AllowWithSubmitRights(string role)
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
    [InlineData("Coordinator", "Start")]
    [InlineData("Student", "Upload")]
    public async Task Submissions_SubmitSubmission_ThrowsForbiddenException(string role, string stepName)
    {
        // Arrange
        const string submissionId = "Start";
        var (controller, instance) = BuildControllerWithRoles([role], submissionId, stepName);

        // Act and Assert
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            controller.SubmitSubmission(instance.Id, submissionId, _ct));
    }

    private (SubmissionsController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string submissionId, string stepName = "Start")
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: stepName)
            .WithEvents(b => b.WithId(submissionId))
            .Build();

        MockInstance(instance);
        MockCurrentUser(roles);

        var controller = new SubmissionsController(_userServiceMock.Object, _modelService, _rightsService,
            _submissionService, _submissionDtoFactory, _workflowInstanceDtoFactory);

        return (controller, instance);
    }
}