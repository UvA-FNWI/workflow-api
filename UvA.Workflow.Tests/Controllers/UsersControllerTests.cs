using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Organizations;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;

namespace UvA.Workflow.Tests.Controllers;

public class UsersControllerTests : ControllerTestsBase
{
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
            _rightsService,
            _eduIdUserServiceMock.Object);

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

        return new UsersController(_userServiceMock.Object,
            _userRepoMock.Object,
            _rightsService,
            _eduIdUserServiceMock.Object);
    }

    private static bool IsConfiguredInternalEmail(string email)
    {
        var domain = email.Trim().Split('@')[^1];
        return new[] { "uva.nl", "auc.nl" }.Any(internalDomain =>
            domain.Equals(internalDomain, StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith($".{internalDomain}", StringComparison.OrdinalIgnoreCase));
    }
}