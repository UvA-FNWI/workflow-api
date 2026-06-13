using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Infrastructure;
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
            new AnswersController(
                _userServiceMock.Object,
                _answerService,
                _answerConversionService,
                _rightsService,
                _externalUserServiceMock.Object,
                _artifactTokenService,
                _submissionDtoFactory,
                _instanceService,
                _modelService,
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

    // File retention on delete and replace.
    // We keep a file in storage only if it was there when the form was last submitted. A file uploaded
    // after that, or while the form was never submitted, is just a draft, so we delete it. Either way the
    // reference on the instance gets removed.

    private static readonly DateTime SubmittedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-60, true, 0)] // uploaded before submission, so it was submitted: keep it
    [InlineData(60, true, 1)] // uploaded after submission, so it's just a draft: delete it
    [InlineData(-60, false, 1)] // form was never submitted: delete it
    public async Task Answers_DeleteArtifact_Single_RetainsOnlyVersionedFiles(
        int createdMinutesFromSubmission, bool submitted, int deleteCalls)
    {
        var instance = BuildInstance(submitted ? SubmittedAt : (DateTime?)null);
        var context = await _answerService.GetQuestionContext(instance.Id, "Start", "Description", _ct);
        SeedSingleFile(instance, context, "art-1", SubmittedAt.AddMinutes(createdMinutesFromSubmission));

        await _answerService.DeleteArtifact(context, "art-1", _ct);

        _artifactServiceMock.Verify(s => s.TryDeleteArtifact("art-1", It.IsAny<CancellationToken>()),
            Times.Exactly(deleteCalls));
        // The reference always goes, whether or not we keep the file.
        Assert.Null(instance.GetProperty(context.Form.PropertyName, "Description"));
    }

    [Theory]
    [InlineData(-60, true, 0)]
    [InlineData(60, true, 1)]
    [InlineData(-60, false, 1)]
    public async Task Answers_DeleteArtifact_Array_RetainsOnlyVersionedFiles(
        int createdMinutesFromSubmission, bool submitted, int deleteCalls)
    {
        var instance = BuildInstance(submitted ? SubmittedAt : (DateTime?)null);
        var baseContext = await _answerService.GetQuestionContext(instance.Id, "Start", "Description", _ct);
        var context = baseContext with
        {
            PropertyDefinition = new PropertyDefinition { Name = "Attachments", Type = "[File]" }
        };

        var keep = new ArtifactInfo("keep-art", "keep.pdf", "application/pdf", 1, SubmittedAt.AddMinutes(-120));
        var remove = new ArtifactInfo("remove-art", "remove.pdf", "application/pdf", 1,
            SubmittedAt.AddMinutes(createdMinutesFromSubmission));
        instance.SetProperty(new BsonArray { keep.ToBsonDocument(), remove.ToBsonDocument() },
            context.Form.PropertyName, "Attachments");

        await _answerService.DeleteArtifact(context, "remove-art", _ct);

        _artifactServiceMock.Verify(s => s.TryDeleteArtifact("remove-art", It.IsAny<CancellationToken>()),
            Times.Exactly(deleteCalls));
        // The file always leaves the array; only the stored bytes might stick around.
        var array = instance.GetProperty(context.Form.PropertyName, "Attachments") as BsonArray;
        Assert.NotNull(array);
        Assert.Equal("keep-art", Assert.Single(array.Select(a => ArtifactInfo.FromBson(a)?.ArtifactId)));
    }

    [Theory]
    [InlineData(-60, true, 0)]
    [InlineData(60, true, 1)]
    [InlineData(-60, false, 1)]
    public async Task Answers_SaveArtifact_Replace_RetainsOnlyVersionedFiles(
        int createdMinutesFromSubmission, bool submitted, int deleteCalls)
    {
        var instance = BuildInstance(submitted ? SubmittedAt : (DateTime?)null);
        var context = await _answerService.GetQuestionContext(instance.Id, "Start", "Description", _ct);
        SeedSingleFile(instance, context, "old-art", SubmittedAt.AddMinutes(createdMinutesFromSubmission));

        var newInfo = new ArtifactInfo("new-art", "new.pdf", "application/pdf", 200, SubmittedAt.AddMinutes(120));
        _artifactServiceMock
            .Setup(s => s.SaveArtifact(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>()))
            .ReturnsAsync(newInfo);

        using var stream = new MemoryStream([1, 2, 3]);
        await _answerService.SaveArtifact(context, "new.pdf", stream, _ct);

        _artifactServiceMock.Verify(s => s.TryDeleteArtifact("old-art", It.IsAny<CancellationToken>()),
            Times.Exactly(deleteCalls));
        // The new file is now the current value.
        Assert.Equal("new-art",
            ArtifactInfo.FromBson(instance.GetProperty(context.Form.PropertyName, "Description"))?.ArtifactId);
    }

    [Theory]
    [InlineData(-60, true, 0)]
    [InlineData(60, true, 1)]
    [InlineData(-60, false, 1)]
    public async Task Answers_SaveAnswer_ClearFile_RetainsOnlyVersionedFiles(
        int createdMinutesFromSubmission, bool submitted, int deleteCalls)
    {
        var instance = BuildInstance(submitted ? SubmittedAt : (DateTime?)null);
        var context = await _answerService.GetQuestionContext(instance.Id, "Start", "Description", _ct);
        SeedSingleFile(instance, context, "art-1", SubmittedAt.AddMinutes(createdMinutesFromSubmission));

        await _answerService.SaveAnswer(context, value: null, UnitTestsHelpers.AdminUser, _ct);

        _artifactServiceMock.Verify(s => s.TryDeleteArtifact("art-1", It.IsAny<CancellationToken>()),
            Times.Exactly(deleteCalls));
    }

    // When a step is rejected, the submission event is suppressed but not removed (RejectSubject suppresses
    // Start), so the form stops counting as submitted. We still want to keep the file that was there at
    // submission time, and still delete one uploaded later. Since retention looks at the submission event's
    // date and not at whether it's currently active, the rejection doesn't change anything.
    [Theory]
    [InlineData(-60, 0)] // was there at the submission that's now suppressed: keep it
    [InlineData(60, 1)] // uploaded after the submission: delete it
    public async Task Answers_DeleteArtifact_RetainsAcrossRejection(
        int createdMinutesFromSubmission, int deleteCalls)
    {
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithEvents(
                b => b.WithId("Start").AsCompleted(SubmittedAt),
                b => b.WithId("RejectSubject").AsCompleted(SubmittedAt.AddMinutes(30)))
            .Build();
        MockInstance(instance);

        var context = await _answerService.GetQuestionContext(instance.Id, "Start", "Description", _ct);
        SeedSingleFile(instance, context, "art-1", SubmittedAt.AddMinutes(createdMinutesFromSubmission));

        await _answerService.DeleteArtifact(context, "art-1", _ct);

        // The form no longer counts as submitted because the rejection suppressed it.
        Assert.False(context.SubmissionState.IsSubmitted);
        // We still keep or delete based on the original submission date.
        _artifactServiceMock.Verify(s => s.TryDeleteArtifact("art-1", It.IsAny<CancellationToken>()),
            Times.Exactly(deleteCalls));
        Assert.Null(instance.GetProperty(context.Form.PropertyName, "Description"));
    }

    private WorkflowInstance BuildInstance(DateTime? submittedAt)
    {
        var builder = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start")
            .WithProperties(("Title", b => b.Value("My Thesis")));
        if (submittedAt is { } date)
            builder.WithEvents(b => b.WithId("Start").AsCompleted(date));

        var instance = builder.Build();
        MockInstance(instance);
        MockEmptyEventLog(instance);
        MockEmptyRelatedInstanceLookups();
        MockCurrentUser("Student");
        return instance;
    }

    private static void SeedSingleFile(WorkflowInstance instance, QuestionContext context, string artifactId,
        DateTime createdOn)
    {
        var info = new ArtifactInfo(artifactId, "old.pdf", "application/pdf", 123, createdOn);
        instance.SetProperty(info.ToBsonDocument(), context.Form.PropertyName, context.PropertyDefinition.Name);
    }
}