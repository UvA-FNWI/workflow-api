using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Versions;
using UvA.Workflow.Tests.Controllers.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class VersionsControllerTests : ControllerTestsBase
{
    private ModelServiceResolver _modelServiceResolver;

    public VersionsControllerTests() : base()
    {
        var _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _modelServiceResolver = new ModelServiceResolver(_httpContextAccessorMock.Object);
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Coordinator")]
    public async Task Versions_CreateVersion_AllowWithAdminRights(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act
        var result = await controller.CreateVersion("version", new Dictionary<string, string>());
        //Assert
        Assert.IsType<OkResult>(result);
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("RandomPerson")]
    public async Task Versions_CreateVersion_ThrowsException(string role)
    {
        // Arrange
        var controller = BuildControllerWithRoles([role]);
        // Act and Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.CreateVersion("version", new Dictionary<string, string>()));
    }

    private VersionsController BuildControllerWithRoles(
        string[] roles)
    {
        _userServiceMock.Setup(s => s.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);
        _userServiceMock.Setup(s => s.GetCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ControllerTestsHelpers.AdminUser);

        return new VersionsController(_modelServiceResolver, _rightsService,
            _loggerFactory.CreateLogger<VersionsController>());
    }
}