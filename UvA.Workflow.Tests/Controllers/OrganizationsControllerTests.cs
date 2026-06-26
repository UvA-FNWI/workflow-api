using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Organizations;
using UvA.Workflow.Api.Organizations.Dtos;
using UvA.Workflow.Organizations;
using UvA.Workflow.Tests.Controllers.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class OrganizationsControllerTests : ControllerTestsBase
{
    private readonly Mock<IOrganizationService> _organizationServiceMock = new();

    [Theory]
    [InlineData("RandomPerson")]
    [InlineData("Api")]
    [InlineData("Coordinator")]
    public async Task Organizations_Create_AllowsLoggedInUsers(string role)
    {
        _organizationServiceMock.Setup(r => r.GetOrCreateOrganization("science", _ct))
            .ReturnsAsync(new Organization { Id = "1", Name = "science" });
        var controller = BuildControllerWithRoles([role]);

        var result = await controller.Create(new CreateOrganizationDto("science"), _ct);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(OrganizationsController.GetById), createdResult.ActionName);

        _organizationServiceMock.Verify(r => r.GetOrCreateOrganization("science", _ct),
            Times.Once);
    }

    [Fact]
    public async Task Organizations_GetById_ReturnsNotFound_WhenMissing()
    {
        _organizationServiceMock.Setup(r => r.GetOrganization("missing", _ct))
            .ReturnsAsync((Organization?)null);
        var controller = BuildControllerWithRoles(["Admin"]);

        var result = await controller.GetById("missing", _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task Organizations_Find_UsesDefaultLimit_WhenLimitIsNull()
    {
        _organizationServiceMock.Setup(r => r.Search("sci", 5, _ct))
            .ReturnsAsync([new Organization { Id = "1", Name = "Science" }]);
        var controller = BuildControllerWithRoles(["Admin"]);

        var result = await controller.Find("sci", null, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IEnumerable<OrganizationDto>>(okResult.Value);
        Assert.Single(dtos);
        _organizationServiceMock.Verify(r => r.Search("sci", 5, _ct), Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(500, 100)]
    public async Task Organizations_Find_ClampsLimit(int requestedLimit, int expectedLimit)
    {
        _organizationServiceMock.Setup(r => r.Search("sci", expectedLimit, _ct))
            .ReturnsAsync([]);
        var controller = BuildControllerWithRoles(["Admin"]);

        _ = await controller.Find("sci", requestedLimit, _ct);

        _organizationServiceMock.Verify(r => r.Search("sci", expectedLimit, _ct), Times.Once);
    }

    private OrganizationsController BuildControllerWithRoles(string[] roles)
    {
        MockCurrentUser(roles);
        return new OrganizationsController(_organizationServiceMock.Object);
    }
}