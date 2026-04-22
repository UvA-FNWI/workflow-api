using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Services;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class AnswersControllerTests : ControllerTestsBase
{
    private readonly SubmissionService _submissionService;
    private readonly SubmissionDtoFactory _submissionDtoFactory;
    private readonly ArtifactTokenService _artifactTokenService;
    private readonly WorkflowInstanceDtoFactory _workflowInstanceDtoFactory;
    private readonly AnswerService _answerService;
    private readonly AnswerConversionService _answerConversionService;

    public AnswersControllerTests() : base()
    {
        _artifactTokenService = new ArtifactTokenService(_configurationMock.Object);
        _submissionService =
            new SubmissionService(_workflowInstanceRepoMock.Object, _modelService, _instanceService,
                _instanceJournalServiceMock.Object, _workflowInstanceService, _jobService, _effectService);
        _submissionDtoFactory =
            new SubmissionDtoFactory(_artifactTokenService, _modelService);
        _workflowInstanceDtoFactory =
            new WorkflowInstanceDtoFactory(_instanceService, _modelService,
                _submissionDtoFactory, _workflowInstanceRepoMock.Object, _rightsService,
                new StepVersionService(_modelService, _eventRepoMock.Object), _workflowInstanceService,
                _loggerFactory.CreateLogger<WorkflowInstanceDtoFactory>());

        _answerConversionService = new AnswerConversionService(_userServiceMock.Object);
        _answerService = new AnswerService(_submissionService, _modelService, _instanceService, _rightsService,
            _artifactServiceMock.Object, _answerConversionService, _instanceEventService.Object,
            _instanceJournalServiceMock.Object);
    }

    [Theory]
    [InlineData("Student", "Start", "AssessmentReviewer")]
    [InlineData("Coordinator", "Start", "AssessmentReviewer")]
    public async Task Answers_GetChoices_AllowWithViewRights(string role, string submissionId, string questionName)
    {
        // Arrange
        var (controller, instance) = BuildControllerWithRoles([role], submissionId);
        // Act
        var result = await controller.GetChoices(instance.Id, submissionId, questionName, _ct);
        //Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("HasNoRights", "Start", "AssessmentReviewer")]
    public async Task Answers_GetChoices_ThrowsForbiddenException(string role, string submissionId, string questionName)
    {
        // Arrange
        var (controller, instance) = BuildControllerWithRoles([role], submissionId);
        // Act and Assert
        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            controller.GetChoices(instance.Id, submissionId, questionName, _ct));
    }

    private (AnswersController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string submissionId)
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithEvents(b => b.WithId(submissionId))
            .WithProperties(("Title", b => b.Value("My Thesis")))
            .Build();

        MockInstance(instance);
        MockEmptyEventLog(instance);
        MockEmptyRelatedInstanceLookups();
        MockCurrentUser(roles);

        var controller =
            new AnswersController(_userServiceMock.Object, _answerService, _rightsService, _artifactTokenService,
                _submissionDtoFactory, _submissionService, _instanceService, _modelService,
                _workflowInstanceRepoMock.Object);

        return (controller, instance);
    }
}