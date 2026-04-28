using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Users;
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
            new WorkflowInstanceDtoFactory(
                _instanceService,
                _modelService,
                _submissionDtoFactory,
                _workflowInstanceRepoMock.Object,
                _rightsService,
                new StepVersionService(_modelService, _eventRepoMock.Object),
                new StepHeaderStatusResolver(_modelService),
                _workflowInstanceService,
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
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<SubmissionDto>(okResult.Value);
        Assert.Equal(submissionId, payload.Id);
        Assert.Equal(instance.Id, payload.InstanceId);
        Assert.Equal(submissionId, payload.FormName);
        Assert.NotNull(payload.Form);
        Assert.NotNull(payload.Answers);
    }

    [Fact]
    public async Task Submissions_SubmitSubmission_DeniedOnInvalidForm()
    {
        // Arrange
        const string submissionId = "Start";
        var (controller, instance) = BuildControllerWithRoles(["Student"], submissionId);

        // Act
        var result = await controller.SubmitSubmission(instance.Id, submissionId, _ct);

        //Assert
        var actionResult = Assert.IsType<ActionResult<SubmitSubmissionResult>>(result);
        var unprocessableResult = Assert.IsType<UnprocessableEntityObjectResult>(actionResult.Result);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, unprocessableResult.StatusCode);
    }

    [Fact]
    public async Task Submissions_SubmitSubmission_AllowedWithValidForm()
    {
        // Arrange
        const string submissionId = "Start";

        // Supply the instance with a full list of properties
        var (controller, instance) = BuildControllerWithRoles(["Student"], submissionId, "Start",
            ("Title", _ => "Title"),
            ("Subject", _ => "Subject"),
            ("Description", _ => new BsonDocument
            {
                { "Name", "Name" }
            }),
            ("Examiner", _ => new BsonDocument()),
            ("Reviewer", _ => new BsonDocument()),
            ("Supervisor", _ => new BsonDocument()),
            ("StartDate", _ => new DateTime(2056, 01, 01, 9, 0, 0, DateTimeKind.Utc)),
            ("EndDate", _ => new DateTime(2057, 01, 01, 9, 0, 0, DateTimeKind.Utc)),
            ("Deadline", _ => new DateTime(2058, 01, 01, 9, 0, 0, DateTimeKind.Utc)),
            ("EC", _ => 1)
        );

        // Act
        var result = await controller.SubmitSubmission(instance.Id, submissionId, _ct);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var submissionResult = Assert.IsType<SubmitSubmissionResult>(okResult.Value);

        Assert.True(submissionResult.Success);
        Assert.True(submissionResult.Success);
        Assert.NotNull(submissionResult.Submission);
        Assert.Equal(submissionId, submissionResult.Submission.Id);
        Assert.Equal(instance.Id, submissionResult.Submission.InstanceId);
        Assert.NotNull(submissionResult.UpdatedInstance);
        Assert.Null(submissionResult.ValidationErrors);
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

    [Fact]
    public async Task Submissions_SubmitSubmission_ReturnsUnauthorized_WhenNoCurrentUser()
    {
        const string submissionId = "Start";
        var (controller, instance) = BuildControllerWithRoles(["Student"], submissionId);

        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await controller.SubmitSubmission(instance.Id, submissionId, _ct);

        Assert.IsType<UnauthorizedResult>(result.Result);
        _instanceJournalServiceMock.Verify(s => s.IncrementVersion(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private (SubmissionsController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string submissionId, string stepName = "Start",
        params (string name, Func<PropertyBuilder, BsonValue> builder)[] props)
    {
        var courseInstance = new WorkflowInstanceBuilder()
            .With("Context", "Start")
            .WithProperties(
                ("Name", _ => "CourseTitle"),
                ("ExternalId", _ => "DummyId"),
                ("Type", _ => "Course"),
                ("Coordinator", _ => new BsonDocument()))
            .Build();

        MockInstance(courseInstance);

        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: stepName)
            .WithEvents(b => b.WithId(submissionId))
            .WithProperties(("Course", _ => courseInstance.Id))
            .WithProperties(props)
            .Build();

        MockInstance(instance);
        MockEmptyEventLog(instance);
        MockCurrentUser(roles);
        MockEmptyRelatedInstanceLookups();

        var controller = new SubmissionsController(_userServiceMock.Object, _modelService, _rightsService,
            _submissionService, _submissionDtoFactory, _workflowInstanceDtoFactory);

        return (controller, instance);
    }
}