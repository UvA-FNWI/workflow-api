using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
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
        _artifactTokenService = new ArtifactTokenService(UnitTestsHelpers.TestS3Config);
        _submissionService =
            new SubmissionService(_workflowInstanceRepoMock.Object, _modelService, _instanceService,
                _instanceJournalServiceMock.Object, _workflowInstanceService, _jobService, _effectService);
        _submissionDtoFactory =
            new SubmissionDtoFactory(_artifactTokenService, _modelService);
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

        _answerConversionService = new AnswerConversionService(
            _userServiceMock.Object,
            _userRepoMock.Object);
        _answerService = new AnswerService(
            _submissionService,
            _modelService,
            _instanceService,
            _rightsService,
            _artifactServiceMock.Object,
            _answerConversionService,
            _instanceEventService.Object,
            _instanceJournalServiceMock.Object);
    }

    [Fact]
    public async Task Answers_GetChoices_AllowWithViewRights()
    {
        var submissionId = "Start";
        // Arrange
        var (controller, instance) = BuildControllerWithRoles(["Coordinator"], submissionId, "SubjectFeedback");
        // Act
        var result = await controller.GetChoices(instance.Id, submissionId, "AssessmentReviewer", _ct);
        //Assert
        var okObjectResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, okObjectResult.StatusCode);
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

    [Fact]
    public async Task Answers_SaveAnswer_CreatesExternalUser_AndSavesAnswer()
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");
        var organization = new Organization("org-1", "External Org");
        var createdExternalUser = new User
        {
            Id = "665f35fb3f1b3c6d4b3d0f12",
            UserName = "external@example.org",
            DisplayName = "External User",
            Email = "external@example.org",
            Organization = organization,
            ProviderKey = "backend-provider"
        };
        _externalUserServiceMock.Setup(s => s.CreateOrUpdateExternalUser(
                "External User",
                "external@example.org",
                organization,
                _ct))
            .ReturnsAsync(new UserSearchResult(
                "external@example.org",
                "External User",
                "external@example.org",
                UserSearchSources.Repository,
                "backend-provider",
                organization));
        _userServiceMock.Setup(s => s.GetUser("external@example.org", It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdExternalUser);

        var result = await controller.SaveAnswer(
            instance.Id,
            "Start",
            "Supervisor",
            new SaveAnswerRequest(
                Value: null,
                ExternalUser: new CreateExternalUserDto("External User", "external@example.org", organization)),
            _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SaveAnswerResponse>(okResult.Value);
        Assert.True(response.Success);
        var pickerUser = Assert.IsType<UserSearchResultDto>(response.User);
        Assert.Equal("external@example.org", pickerUser.UserName);
        Assert.Equal("External User", pickerUser.DisplayName);
        Assert.Equal("external@example.org", pickerUser.Email);
        Assert.Equal(UserSearchSources.Repository, pickerUser.SourceKey);
        Assert.Same(organization, pickerUser.Organization);
        Assert.True(pickerUser.IsExternal);
        Assert.DoesNotContain(pickerUser.GetType().GetProperties(), p => p.Name == "ProviderKey");

        var answer = Assert.Single(response.Answers, a => a.QuestionName == "Supervisor");
        Assert.True(answer.Value.HasValue);
        Assert.Equal("external@example.org", answer.Value.Value.GetProperty("userName").GetString());
        Assert.Equal("External User", answer.Value.Value.GetProperty("displayName").GetString());
        Assert.Equal("external@example.org", answer.Value.Value.GetProperty("email").GetString());
        Assert.True(answer.Value.Value.GetProperty("isExternal").GetBoolean());

        var submissionAnswer = Assert.Single(response.Submission.Answers, a => a.QuestionName == "Supervisor");
        Assert.True(submissionAnswer.Value.HasValue);
        Assert.Equal("external@example.org", submissionAnswer.Value.Value.GetProperty("userName").GetString());
        Assert.Equal("External User", submissionAnswer.Value.Value.GetProperty("displayName").GetString());
        Assert.Equal("external@example.org", submissionAnswer.Value.Value.GetProperty("email").GetString());
        Assert.True(submissionAnswer.Value.Value.GetProperty("isExternal").GetBoolean());
    }

    [Fact]
    public async Task Answers_SaveAnswer_WithExternalUser_ThrowsForbiddenWithoutEditRights()
    {
        var (controller, instance) = BuildControllerWithRoles(["HasNoRights"], "Start");

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            controller.SaveAnswer(
                instance.Id,
                "Start",
                "Supervisor",
                new SaveAnswerRequest(
                    Value: null,
                    ExternalUser: new CreateExternalUserDto("External User", "external@example.org")),
                _ct));
    }

    [Fact]
    public async Task Answers_SaveAnswer_WithExternalUser_RejectsNonUserQuestion()
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");

        var result = await controller.SaveAnswer(
            instance.Id,
            "Start",
            "Title",
            new SaveAnswerRequest(
                Value: null,
                ExternalUser: new CreateExternalUserDto("External User", "external@example.org")),
            _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("InvalidQuestionType", error.ErrorCode);
        Assert.Equal("InvalidQuestionType", error.Message);
    }

    [Theory]
    [InlineData("Reviewer")]
    [InlineData("Examiner")]
    public async Task Answers_SaveAnswer_WithExternalUser_RejectsUserQuestion_WhenExternalUsersAreNotAllowed(
        string questionName)
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");

        var result = await controller.SaveAnswer(
            instance.Id,
            "Start",
            questionName,
            new SaveAnswerRequest(
                Value: null,
                ExternalUser: new CreateExternalUserDto("External User", "external@example.org")),
            _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("ExternalUsersNotAllowed", error.ErrorCode);
        Assert.Equal("ExternalUsersNotAllowed", error.Message);
    }

    [Theory]
    [InlineData(ExternalUserCreationFailureReason.InvalidEmailAddress, StatusCodes.Status400BadRequest,
        "InvalidEmailAddress", "InvalidEmailAddress")]
    [InlineData(ExternalUserCreationFailureReason.InternalEmailAddress, StatusCodes.Status400BadRequest,
        "ManualUserInternalEmail", "ManualUserInternalEmail")]
    [InlineData(ExternalUserCreationFailureReason.UserAlreadyExists, StatusCodes.Status409Conflict,
        "ManualUserEmailAlreadyExists", "ManualUserEmailAlreadyExists")]
    public async Task Answers_SaveAnswer_WithExternalUser_MapsExternalUserErrors(
        ExternalUserCreationFailureReason reason,
        int statusCode,
        string expectedCode,
        string expectedMessage)
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");
        _externalUserServiceMock.Setup(s => s.CreateOrUpdateExternalUser(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Organization?>(),
                _ct))
            .ThrowsAsync(new ExternalUserCreationException(reason, "Service error"));

        var result = await controller.SaveAnswer(
            instance.Id,
            "Start",
            "Supervisor",
            new SaveAnswerRequest(
                Value: null,
                ExternalUser: new CreateExternalUserDto("External User", "external@example.org")),
            _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(statusCode, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal(expectedCode, error.ErrorCode);
        Assert.Equal(expectedMessage, error.Message);
    }

    private (AnswersController Controller, WorkflowInstance Instance) BuildControllerWithRoles(
        string[] roles, string submissionId, string stepName = "Start")
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: stepName)
            .WithEvents(b => b.WithId(submissionId))
            .WithProperties(("Title", b => b.Value("My Thesis")))
            .Build();

        MockInstance(instance);
        MockEmptyEventLog(instance);
        MockEmptyRelatedInstanceLookups();
        MockCurrentUser(roles);

        var controller =
            new AnswersController(_userServiceMock.Object, _answerService, _answerConversionService, _rightsService,
                _externalUserServiceMock.Object, _artifactTokenService,
                _submissionDtoFactory, _submissionService, _instanceService, _modelService,
                _workflowInstanceRepoMock.Object);

        return (controller, instance);
    }

    [Fact]
    public async Task Answers_SaveAnswer_RejectsSelectedExternalUser_WhenExternalUsersAreNotAllowed()
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");
        _userServiceMock.Setup(s => s.GetUser("external@example.org", _ct))
            .ReturnsAsync(new User
            {
                UserName = "external@example.org",
                DisplayName = "External User",
                Email = "external@example.org",
                ProviderKey = "eduid"
            });

        var result = await controller.SaveAnswer(
            instance.Id,
            "Start",
            "Reviewer",
            new SaveAnswerRequest(
                Value: JsonSerializer.SerializeToElement(new UserSearchResultDto(
                    "external@example.org",
                    "External User",
                    "external@example.org",
                    UserSearchSources.Repository,
                    null,
                    true))),
            _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("ExternalUsersNotAllowed", error.ErrorCode);
    }

    [Fact]
    public async Task Answers_SaveAnswer_AllowsSelectedExternalUser_WhenExternalUsersAreAllowed()
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");
        _userServiceMock.Setup(s => s.GetUser("external@example.org", _ct))
            .ReturnsAsync(new User
            {
                UserName = "external@example.org",
                DisplayName = "External User",
                Email = "external@example.org",
                ProviderKey = "eduid"
            });

        var result = await controller.SaveAnswer(
            instance.Id,
            "Start",
            "Supervisor",
            new SaveAnswerRequest(
                Value: JsonSerializer.SerializeToElement(new UserSearchResultDto(
                    "external@example.org",
                    "External User",
                    "external@example.org",
                    UserSearchSources.Repository,
                    null,
                    true))),
            _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SaveAnswerResponse>(okResult.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task Answers_SaveAnswer_AllowsSelectedInternalUser_WhenExternalUsersAreNotAllowed()
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");
        _userServiceMock.Setup(s => s.GetUser("internal-123", _ct))
            .ReturnsAsync(new User
            {
                UserName = "internal-123",
                DisplayName = "Internal User",
                Email = "internal@example.org",
                ProviderKey = UserProviderKeys.Internal
            });

        var result = await controller.SaveAnswer(
            instance.Id,
            "Start",
            "Reviewer",
            new SaveAnswerRequest(
                Value: JsonSerializer.SerializeToElement(new UserSearchResultDto(
                    "internal-123",
                    "Internal User",
                    "internal@example.org",
                    UserSearchSources.Repository,
                    null,
                    false))),
            _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<SaveAnswerResponse>(okResult.Value);
        Assert.True(response.Success);
    }
}