using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using MongoDB.Driver;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Organizations;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class UsersControllerTests : ControllerTestsBase
{
    private const string InstanceId = "instance-id";
    private const string ExternalUserId = "665f35fb3f1b3c6d4b3d0f12";

    private readonly AnswerService _answerService;
    private readonly ExternalUserEmailUpdateService _externalUserEmailUpdateService;

    public UsersControllerTests()
    {
        _answerService = new AnswerService(
            new SubmissionService(_workflowInstanceRepoMock.Object, _modelService, _instanceService,
                _instanceJournalServiceMock.Object, _workflowInstanceService, _jobService, _effectService),
            _modelService,
            _instanceService,
            _rightsService,
            _artifactServiceMock.Object,
            new AnswerConversionService(_userServiceMock.Object, _userRepoMock.Object),
            _instanceEventService.Object,
            _instanceJournalServiceMock.Object,
            _userServiceMock.Object,
            _externalUserServiceMock.Object);
        _externalUserEmailUpdateService = new ExternalUserEmailUpdateService(
            _rightsService,
            _answerService,
            _modelService,
            _instanceService);
    }

    [Theory]
    [InlineData("Coordinator")]
    [InlineData("Student")]
    [InlineData("Admin")]
    [InlineData("Api")]
    public async Task Users_GetLoggedInUser_Allow(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.GetLoggedInUser(_ct);
        //Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var userDto = Assert.IsType<UserDto>(okResult.Value);

        Assert.Equal(UnitTestsHelpers.AdminUser.Id, userDto.Id);
        Assert.Equal(UnitTestsHelpers.AdminUser.Email, userDto.Email);
        Assert.Equal(UnitTestsHelpers.AdminUser.UserName, userDto.UserName);
        Assert.Equal(UnitTestsHelpers.AdminUser.DisplayName, userDto.DisplayName);
    }

    [Theory]
    [InlineData("SystemAdmin", true)]
    [InlineData("Coordinator", false)]
    [InlineData("Student", false)]
    [InlineData("RandomPerson", false)]
    public async Task Users_GetLoggedInUser_SetsIsSuperAdmin_ForSystemAdminRole(string role, bool expectedIsSuperAdmin)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.GetLoggedInUser(_ct);
        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var userDto = Assert.IsType<UserDto>(okResult.Value);
        Assert.Equal(expectedIsSuperAdmin, userDto.IsSuperAdmin);
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("RandomPerson")]
    public async Task Users_Create_ThrowUnauthorizedException(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act and Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.Create(new CreateUserDto("username", "displayName", "email@example.org"), _ct));
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Coordinator")]
    public async Task Users_Create_AllowedWithViewAdminRights(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.Create(new CreateUserDto("username", "displayName", "email@example.org"), _ct);
        //Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);

        Assert.Equal(nameof(UsersController.GetById), createdResult.ActionName);

        _userRepoMock.Verify(r => r.Create(It.Is<User>(u =>
            u.UserName == "username" &&
            u.DisplayName == "displayName" &&
            u.Email == "email@example.org"), _ct), Times.Once);
    }

    [Fact]
    public async Task Users_Create_ReturnsConflict_WhenEmailAlreadyExists()
    {
        var controller = BuildControllerWithRoles(["Api"]);
        _userRepoMock.Setup(r => r.GetByEmail("duplicate@example.org", _ct))
            .ReturnsAsync(new User { Email = "DUPLICATE@example.org" });

        var result = await controller.Create(new CreateUserDto("username",
            "displayName",
            "duplicate@example.org"), _ct);

        var conflictResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
        var error = Assert.IsType<Error>(conflictResult.Value);
        Assert.Equal("ManualUserEmailAlreadyExists", error.ErrorCode);
        Assert.Equal("Email already exists", error.Message);
        _userRepoMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("student@uva.nl")]
    [InlineData("student@sub.uva.nl")]
    public async Task Users_Create_RejectsInternalEmail(string email)
    {
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.Create(new CreateUserDto("username", "displayName", email), _ct);

        var badRequestResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var error = Assert.IsType<Error>(badRequestResult.Value);
        Assert.Equal("ManualUserInternalEmail", error.ErrorCode);
        Assert.Equal("Internal email address", error.Message);
        _userRepoMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_Create_TrimsEmailBeforePersisting()
    {
        var controller = BuildControllerWithRoles(["Api"]);

        await controller.Create(new CreateUserDto("username", "displayName", "  doctor@amsterdamumc.nl  "), _ct);

        _userRepoMock.Verify(r => r.GetByEmail("doctor@amsterdamumc.nl", _ct), Times.Once);
        _userRepoMock.Verify(r => r.Create(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        var createdUser = Assert.IsType<User>(_userRepoMock.Invocations
            .Single(i => i.Method.Name == nameof(IUserRepository.Create))
            .Arguments[0]);
        Assert.Equal("doctor@amsterdamumc.nl", createdUser.Email);
    }

    [Fact]
    public async Task Users_UpdateEmail_UpdatesRequiredExternalUser()
    {
        var user = new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required,
            IsActive = false
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        var controller = BuildControllerWithRoles(["Api"]);
        _userServiceMock.Setup(s => s.GetUser("new@example.org", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await controller.UpdateEmail(user.Id,
            new UpdateUserEmailDto("  new@example.org  ", InstanceId), _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDto>(okResult.Value);
        Assert.Equal("new@example.org", dto.Email);
        Assert.Equal("new@example.org", dto.UserName);
        Assert.True(dto.RequiresInvitation);
        _userRepoMock.Verify(r => r.Update(It.Is<User>(u =>
            u.Id == user.Id &&
            u.Email == "new@example.org" &&
            u.UserName == "new@example.org"), _ct), Times.Once);
    }

    [Fact]
    public async Task Users_UpdateEmail_UpdatesEveryEditableUserReferenceInInstance()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var user = new User
        {
            Id = userId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required,
            IsActive = false
        };
        var instance = new WorkflowInstanceBuilder()
            .With(workflowDefinition: "Project", currentStep: "Start", id: InstanceId)
            .WithProperties(
                ("Examiner", _ => new BsonDocument
                {
                    { "_id", ObjectId.Parse(userId) },
                    { "UserName", "old@example.org" },
                    { "DisplayName", "External User" },
                    { "Email", "old@example.org" }
                }),
                ("Reviewer", _ => new BsonDocument
                {
                    { "_id", ObjectId.Parse(userId) },
                    { "UserName", "old@example.org" },
                    { "DisplayName", "External User" },
                    { "Email", "old@example.org" }
                }))
            .WithEvent("Start", DateTime.UtcNow)
            .Build();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        var controller = BuildControllerWithRoles(["Api"]);
        _workflowInstanceRepoMock.Setup(r => r.GetById(InstanceId, _ct)).ReturnsAsync(instance);
        _userServiceMock.Setup(s => s.GetUser("new@example.org", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await controller.UpdateEmail(user.Id,
            new UpdateUserEmailDto("new@example.org", InstanceId), _ct);

        Assert.Equal("new@example.org", instance.Properties["Examiner"].AsBsonDocument["Email"].AsString);
        Assert.Equal("new@example.org", instance.Properties["Examiner"].AsBsonDocument["UserName"].AsString);
        Assert.Equal("new@example.org", instance.Properties["Reviewer"].AsBsonDocument["Email"].AsString);
        Assert.Equal("new@example.org", instance.Properties["Reviewer"].AsBsonDocument["UserName"].AsString);
        _workflowInstanceRepoMock.Verify(r => r.UpdateFields(instance.Id,
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct), Times.Exactly(2));
    }

    [Fact]
    public async Task Users_UpdateEmail_ReturnsNotFound_WhenUserDoesNotExist()
    {
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateEmail("missing-user-id",
            new UpdateUserEmailDto("new@example.org", InstanceId), _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("internal", UserInvitationState.Required)]
    [InlineData("eduid", UserInvitationState.Pending)]
    [InlineData("eduid", UserInvitationState.Completed)]
    public async Task Users_UpdateEmail_RejectsUsersThatAreNotEligible(
        string providerKey,
        UserInvitationState invitationState)
    {
        var user = new User
        {
            Id = "user-id",
            UserName = "old@example.org",
            DisplayName = "User",
            Email = "old@example.org",
            ProviderKey = providerKey,
            InvitationState = invitationState
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateEmail(user.Id,
            new UpdateUserEmailDto("new@example.org", InstanceId), _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("UserEmailUpdateNotAllowed", error.ErrorCode);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_UpdateEmail_RejectsDuplicateEmailFromAnotherUser()
    {
        var user = new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetByEmail("duplicate@example.org", _ct))
            .ReturnsAsync(new User { Id = "other-user-id", Email = "duplicate@example.org" });
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateEmail(user.Id,
            new UpdateUserEmailDto("duplicate@example.org", InstanceId), _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal("ManualUserEmailAlreadyExists", error.ErrorCode);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_UpdateEmail_AllowsSameEmailForSameUser()
    {
        var user = new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetByEmail("old@example.org", _ct)).ReturnsAsync(user);
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateEmail(user.Id,
            new UpdateUserEmailDto("old@example.org", InstanceId), _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<UserDto>(okResult.Value);
        Assert.Equal("old@example.org", dto.Email);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _workflowInstanceRepoMock.Verify(r => r.UpdateFields(It.IsAny<string>(),
            It.IsAny<UpdateDefinition<WorkflowInstance>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("student@uva.nl", StatusCodes.Status400BadRequest, "ManualUserInternalEmail")]
    [InlineData("not-an-email", StatusCodes.Status400BadRequest, "InvalidEmailAddress")]
    public async Task Users_UpdateEmail_RejectsInvalidTargetEmail(
        string email,
        int expectedStatusCode,
        string expectedErrorCode)
    {
        var user = new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required
        };
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        var controller = BuildControllerWithRoles(["Api"]);

        var result = await controller.UpdateEmail(user.Id,
            new UpdateUserEmailDto(email, InstanceId), _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        var error = Assert.IsType<Error>(objectResult.Value);
        Assert.Equal(expectedErrorCode, error.ErrorCode);
        _userRepoMock.Verify(r => r.Update(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_UpdateEmail_RequiresAnswerEditRights()
    {
        _userRepoMock.Setup(r => r.GetById(ExternalUserId, _ct)).ReturnsAsync(new User
        {
            Id = ExternalUserId,
            UserName = "old@example.org",
            DisplayName = "External User",
            Email = "old@example.org",
            ProviderKey = "eduid",
            InvitationState = UserInvitationState.Required
        });
        var controller = BuildControllerWithRoles(["HasNoRights"]);

        var result = await controller.UpdateEmail(ExternalUserId,
            new UpdateUserEmailDto("new@example.org", InstanceId), _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Theory]
    [InlineData("student@uva.nl")]
    [InlineData("student@sub.uva.nl")]
    [InlineData("STUDENT@SUB.UVA.NL")]
    public async Task Users_VerifyEmail_RejectsInternalEmail(string email)
    {
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.VerifyEmail(new VerifyEmailRequest(email), _ct);

        var badRequestResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var error = Assert.IsType<Error>(badRequestResult.Value);
        Assert.Equal("ManualUserInternalEmail", error.ErrorCode);
        Assert.Equal("Internal email address", error.Message);
        _userRepoMock.Verify(r => r.GetByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("doctor@amsterdamumc.nl")]
    [InlineData("doctor@dept.amsterdamumc.nl")]
    public async Task Users_VerifyEmail_AllowsAmsterdamUmcEmail(string email)
    {
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.VerifyEmail(new VerifyEmailRequest(email), _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<VerifyEmailResponse>(okResult.Value);
        Assert.Equal(email, response.Email);
        Assert.Equal("Valid", response.Status);
        _userRepoMock.Verify(r => r.GetByEmail(email, _ct), Times.Once);
    }

    [Fact]
    public async Task Users_VerifyEmail_RejectsDuplicateEmail()
    {
        var controller = BuildControllerWithRoles(["Student"]);
        _userRepoMock.Setup(r => r.GetByEmail("Duplicate@Example.Org", _ct))
            .ReturnsAsync(new User { Email = "DUPLICATE@example.org" });

        var result = await controller.VerifyEmail(new VerifyEmailRequest("Duplicate@Example.Org"), _ct);

        var conflictResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
        var error = Assert.IsType<Error>(conflictResult.Value);
        Assert.Equal("ManualUserEmailAlreadyExists", error.ErrorCode);
        Assert.Equal("Email already exists", error.Message);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Users_VerifyEmail_RejectsInvalidEmailFormat(string email)
    {
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.VerifyEmail(new VerifyEmailRequest(email), _ct);

        var badRequestResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
        var error = Assert.IsType<Error>(badRequestResult.Value);
        Assert.Equal("InvalidEmailAddress", error.ErrorCode);
        Assert.Equal("Invalid email address", error.Message);
        _userRepoMock.Verify(r => r.GetByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Users_VerifyEmail_TrimsEmailBeforeValidation()
    {
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.VerifyEmail(new VerifyEmailRequest("  doctor@amsterdamumc.nl  "), _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<VerifyEmailResponse>(okResult.Value);
        Assert.Equal("doctor@amsterdamumc.nl", response.Email);
        _userRepoMock.Verify(r => r.GetByEmail("doctor@amsterdamumc.nl", _ct), Times.Once);
    }

    [Fact]
    public async Task Users_GetLoggedInUser_ReturnsNotFound_WhenNoUserIsAuthenticated()
    {
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var controller = new UsersController(_userServiceMock.Object,
            _userRepoMock.Object,
            _workflowInstanceRepoMock.Object,
            _rightsService,
            _eduIdUserServiceMock.Object,
            null!,
            null!,
            _externalUserEmailUpdateService,
            Mock.Of<ILogger<UsersController>>());

        var result = await controller.GetLoggedInUser(_ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task Users_Find_PreservesExternalFlag()
    {
        _userServiceMock.Setup(s => s.FindUsers("external", true, _ct))
            .ReturnsAsync([
                new UserSearchResult("external-123",
                    "External User",
                    "external@example.org",
                    "eduid",
                    "eduid")
            ]);
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.Find("external", true, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.Single(Assert.IsAssignableFrom<IEnumerable<UserSearchResultDto>>(okResult.Value));
        Assert.True(user.IsExternal);
    }

    [Fact]
    public async Task Users_Find_PassesThroughOrganizationFromSearchSource()
    {
        var organization = Organization.Create("FNWI");
        // The organisation is now sourced upstream (the DataNose search source); the controller
        // must surface whatever the search returns without modifying it.
        _userServiceMock.Setup(s => s.FindUsers("internal", true, _ct))
            .ReturnsAsync([
                new UserSearchResult("internal-123",
                    "Internal User",
                    "internal@uva.nl",
                    UserSearchSources.Repository,
                    Organization: organization)
            ]);
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.Find("internal", true, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.Single(Assert.IsAssignableFrom<IEnumerable<UserSearchResultDto>>(okResult.Value));
        Assert.NotNull(user.Organization);
        Assert.Equal(organization.Id, user.Organization!.Id);
        Assert.Equal(organization.Name, user.Organization.Name);
        Assert.False(user.IsExternal);
    }

    [Fact]
    public async Task Users_Find_DoesNotInventOrganizationWhenSearchReturnsNone()
    {
        _userServiceMock.Setup(s => s.FindUsers("external", true, _ct))
            .ReturnsAsync([
                new UserSearchResult("external-123",
                    "External User",
                    "external@example.org",
                    UserSearchSources.Repository,
                    "eduid")
            ]);
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.Find("external", true, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.Single(Assert.IsAssignableFrom<IEnumerable<UserSearchResultDto>>(okResult.Value));
        Assert.Null(user.Organization);
        Assert.True(user.IsExternal);
    }

    [Fact]
    public async Task Users_Find_DefaultsToIncludingExternalUsers()
    {
        _userServiceMock.Setup(s => s.FindUsers("external", true, _ct))
            .ReturnsAsync([]);
        var controller = BuildControllerWithRoles(["Student"]);

        await controller.Find("external", ct: _ct);

        _userServiceMock.Verify(s => s.FindUsers("external", true, _ct), Times.Once);
    }

    [Fact]
    public async Task Users_Find_CanExcludeExternalUsers()
    {
        _userServiceMock.Setup(s => s.FindUsers("external", false, _ct))
            .ReturnsAsync([
                new UserSearchResult("internal-123",
                    "Internal User",
                    "internal@example.org",
                    UserSearchSources.Repository)
            ]);
        var controller = BuildControllerWithRoles(["Student"]);

        var result = await controller.Find("external", false, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.Single(Assert.IsAssignableFrom<IEnumerable<UserSearchResultDto>>(okResult.Value));
        Assert.False(user.IsExternal);
        _userServiceMock.Verify(s => s.FindUsers("external", false, _ct), Times.Once);
    }

    private UsersController BuildControllerWithRoles(
        string[] roles)
    {
        MockCurrentUser(roles);
        _eduIdUserServiceMock.Setup(s => s.IsInternalEmailAddress(It.IsAny<string>()))
            .Returns((string email) => IsConfiguredInternalEmail(email));
        _workflowInstanceRepoMock.Setup(r => r.GetById(InstanceId, _ct))
            .ReturnsAsync(new WorkflowInstanceBuilder()
                .With(workflowDefinition: "Project", currentStep: "Start", id: InstanceId)
                .WithProperties(("Supervisor", _ => new BsonDocument
                {
                    { "_id", ObjectId.Parse(ExternalUserId) },
                    { "UserName", "old@example.org" },
                    { "DisplayName", "External User" },
                    { "Email", "old@example.org" }
                }))
                .WithEvent("Start", DateTime.UtcNow)
                .Build());
        _workflowInstanceRepoMock.Setup(r => r.UpdateFields(InstanceId,
                It.IsAny<UpdateDefinition<WorkflowInstance>>(), _ct))
            .Returns(Task.CompletedTask);
        _userServiceMock.Setup(s => s.GetUser(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userName, CancellationToken _) => new User
            {
                Id = ExternalUserId,
                UserName = userName,
                DisplayName = "External User",
                Email = userName,
                ProviderKey = "eduid",
                InvitationState = UserInvitationState.Required
            });

        return new UsersController(_userServiceMock.Object,
            _userRepoMock.Object,
            _workflowInstanceRepoMock.Object,
            _rightsService,
            _eduIdUserServiceMock.Object,
            null!,
            null!,
            _externalUserEmailUpdateService,
            Mock.Of<ILogger<UsersController>>());
    }

    private static bool IsConfiguredInternalEmail(string email)
    {
        var domain = email.Trim().Split('@')[^1];
        return new[] { "uva.nl", "auc.nl" }.Any(internalDomain =>
            domain.Equals(internalDomain, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith($".{internalDomain}", StringComparison.OrdinalIgnoreCase));
    }
}