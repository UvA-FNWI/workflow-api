using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Journaling;
using UvA.Workflow.Organizations;
using UvA.Workflow.Persistence;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.Versioning;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class AnswersControllerTests : ControllerTestsBase
{
    private readonly Mock<IOrganizationService> _organizationServiceMock = new();
    private readonly SubmissionDtoFactory _submissionDtoFactory;
    private readonly ArtifactTokenService _artifactTokenService;
    private readonly WorkflowInstanceDtoFactory _workflowInstanceDtoFactory;
    private readonly AnswerService _answerService;
    private readonly AnswerConversionService _answerConversionService;

    public AnswersControllerTests() : base()
    {
        _artifactTokenService = new ArtifactTokenService(UnitTestsHelpers.TestS3Config);
        _submissionDtoFactory =
            new SubmissionDtoFactory(_artifactTokenService, _modelService);
        _workflowInstanceDtoFactory =
            new WorkflowInstanceDtoFactory(
                _instanceService,
                _modelService,
                _submissionDtoFactory,
                _rightsService,
                new StepVersionService(),
                new StepHeaderStatusResolver(_modelService),
                _workflowInstanceService,
                _loggerFactory.CreateLogger<WorkflowInstanceDtoFactory>());

        _answerConversionService = new AnswerConversionService(
            _userServiceMock.Object,
            _userRepoMock.Object);
        _answerService = new AnswerService(
            _modelService,
            _instanceService,
            _rightsService,
            _artifactServiceMock.Object,
            _answerConversionService,
            _workflowInstanceService,
            _instanceEventService.Object,
            _instanceJournalServiceMock.Object,
            _userServiceMock.Object,
            _externalUserServiceMock.Object);
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
        var organization = new Organization { Id = ObjectId.GenerateNewId().ToString(), Name = "External Org" };
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
                null,
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
                ExternalUser: new ExternalUserDto("External User", "external@example.org", organization)),
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
    public async Task Answers_ClearAnswers_ClearsEveryFormAnswerAndPreservesOtherProperties()
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");
        const string artifactId = "artifact-1";
        instance.Properties["Subject"] = "Original subject";
        instance.Properties["Description"] = new ArtifactInfo(artifactId, "description.pdf").ToBsonDocument();
        instance.Properties["CanBePublished"] = true;

        var result = await controller.ClearAnswers(instance.Id, "Start", _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var submission = Assert.IsType<SubmissionDto>(okResult.Value);
        Assert.All(submission.Answers, answer => Assert.False(answer.Value.HasValue));
        Assert.All(submission.Answers.Where(answer => answer.Files != null),
            answer => Assert.Empty(answer.Files!));
        Assert.All(instance.Properties.Where(property => property.Key is "Title" or "Subject" or "Description"),
            property => Assert.True(property.Value.IsBsonNull));
        Assert.True(instance.Properties["CanBePublished"].AsBoolean);
        _artifactServiceMock.Verify(service => service.TryDeleteArtifact(artifactId, _ct), Times.Once);
        _workflowInstanceRepoMock.Verify(repository => repository.UpdateFields(instance.Id,
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Exactly(3));
    }

    [Fact]
    public async Task Answers_ClearAnswers_ThrowsForbiddenWithoutEditRights()
    {
        var (controller, instance) = BuildControllerWithRoles(["HasNoRights"], "Start");

        await Assert.ThrowsAsync<ForbiddenWorkflowActionException>(() =>
            controller.ClearAnswers(instance.Id, "Start", _ct));
    }

    [Fact]
    public async Task Answers_ClearAnswers_PreservesFilesFromEarlierSubmittedVersions()
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");
        const string artifactId = "submitted-artifact";
        instance.Properties["Description"] = new ArtifactInfo(artifactId, "description.pdf").ToBsonDocument();
        _instanceEventService.Setup(service =>
                service.WasEventEverTriggered(instance.Id, "Start", _ct))
            .ReturnsAsync(true);
        _instanceJournalServiceMock.Setup(service => service.LogPropertyChange(instance.Id,
                It.Is<PropertyChangeEntry>(entry => entry.Path == "Description"), _ct))
            .ReturnsAsync(false);

        await controller.ClearAnswers(instance.Id, "Start", _ct);

        _instanceJournalServiceMock.Verify(service => service.LogPropertyChange(instance.Id,
            It.Is<PropertyChangeEntry>(entry => entry.Path == "Description"), _ct), Times.Once);
        _artifactServiceMock.Verify(service =>
            service.TryDeleteArtifact(artifactId, It.IsAny<CancellationToken>()), Times.Never);
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
                    ExternalUser: new ExternalUserDto("External User", "external@example.org")),
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
                ExternalUser: new ExternalUserDto("External User", "external@example.org")),
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
                ExternalUser: new ExternalUserDto("External User", "external@example.org")),
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
                It.IsAny<string?>(),
                _ct))
            .ThrowsAsync(new ExternalUserCreationException(reason, "Service error"));

        var result = await controller.SaveAnswer(
            instance.Id,
            "Start",
            "Supervisor",
            new SaveAnswerRequest(
                Value: null,
                ExternalUser: new ExternalUserDto("External User", "external@example.org")),
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
            new AnswersController(
                _answerService,
                _rightsService,
                _artifactTokenService,
                _submissionDtoFactory,
                _instanceService,
                _modelService,
                _workflowInstanceRepoMock.Object,
                _workflowInstanceService);

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

    [Fact]
    public async Task Answers_SaveAnswer_Journal_RecordsRealAdmin_WhenImpersonating()
    {
        var (controller, instance) = BuildControllerWithRoles(["Student"], "Start");
        MockImpersonation("Student");

        // Simulate the form having been submitted before so shouldLog=true reaches SavePropertyValue
        _instanceEventService
            .Setup(s => s.WasEventEverTriggered(
                instance.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        PropertyChangeEntry? capturedEntry = null;
        _instanceJournalServiceMock
            .Setup(j => j.LogPropertyChange(
                It.IsAny<string>(), It.IsAny<PropertyChangeEntry>(), It.IsAny<CancellationToken>()))
            .Callback<string, PropertyChangeEntry, CancellationToken>((_, entry, _) => capturedEntry = entry)
            .ReturnsAsync(false);

        await controller.SaveAnswer(
            instance.Id, "Start", "Title",
            new SaveAnswerRequest(Value: JsonSerializer.SerializeToElement("New Title")),
            _ct);

        Assert.NotNull(capturedEntry);
        Assert.Equal(UnitTestsHelpers.AdminUser.UserName, capturedEntry.ModifiedBy);
        Assert.NotEqual(UnitTestsHelpers.ImpersonatedTarget.UserName, capturedEntry.ModifiedBy);
    }
}