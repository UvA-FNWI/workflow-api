using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;
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

        Assert.Equal(ControllerTestsHelpers.AdminUser.Id, userDto.Id);
        Assert.Equal(ControllerTestsHelpers.AdminUser.Email, userDto.Email);
        Assert.Equal(ControllerTestsHelpers.AdminUser.UserName, userDto.UserName);
        Assert.Equal(ControllerTestsHelpers.AdminUser.DisplayName, userDto.DisplayName);
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
            controller.Create(new CreateUserDto("username", "displayName", "email"), _ct));
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Coordinator")]
    public async Task Users_Create_AllowedWithViewAdminRights(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.Create(new CreateUserDto("username", "displayName", "email"), _ct);
        //Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);

        Assert.Equal(nameof(UsersController.GetById), createdResult.ActionName);

        _userRepoMock.Verify(r => r.Create(It.Is<User>(u =>
            u.UserName == "username" &&
            u.DisplayName == "displayName" &&
            u.Email == "email"), _ct), Times.Once);
    }

    [Fact]
    public async Task Users_GetLoggedInUser_ReturnsNotFound_WhenNoUserIsAuthenticated()
    {
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var controller = new UsersController(_userServiceMock.Object, _userRepoMock.Object, _rightsService);

        var result = await controller.GetLoggedInUser(_ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    private UsersController BuildControllerWithRoles(
        string[] roles)
    {
        MockCurrentUser(roles);
        return new UsersController(_userServiceMock.Object, _userRepoMock.Object, _rightsService);
    }
}