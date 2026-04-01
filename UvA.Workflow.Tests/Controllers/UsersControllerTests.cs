using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Users;
using UvA.Workflow.Api.Users.Dtos;

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
        Assert.IsType<ActionResult<UserDto>>(result);
    }

    [Theory]
    [InlineData("Coordinator")]
    [InlineData("Student")]
    public async Task Users_Create_ThrowUnauthorizedException(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act and Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.Create(new CreateUserDto("username", "displayName", "email"), _ct));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Api")]
    public async Task Users_Create_AdminOrApiAllowed(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.Create(new CreateUserDto("username", "displayName", "email"), _ct);
        //Assert
        Assert.IsType<ActionResult<UserDto>>(result);
    }

    private UsersController BuildControllerWithRoles(
        string[] roles)
    {
        _userServiceMock.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ControllerTestsHelpers.AdminUser);

        return new UsersController(_userServiceMock.Object, _userRepoMock.Object);
    }
}