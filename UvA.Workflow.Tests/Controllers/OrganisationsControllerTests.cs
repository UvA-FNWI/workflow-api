using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Organisations;
using UvA.Workflow.Api.Organisations.Dtos;
using UvA.Workflow.Organisations;
using UvA.Workflow.Tests.Controllers.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class OrganisationsControllerTests : ControllerTestsBase
{
    private readonly Mock<IOrganisationRepository> _organisationRepositoryMock = new();

    [Theory]
    [InlineData("Student")]
    [InlineData("RandomPerson")]
    public async Task Organisations_Create_ThrowsUnauthorizedException(string role)
    {
        var controller = BuildControllerWithRoles([role]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.Create(new CreateOrganisationDto("science"), _ct));
    }

    [Theory]
    [InlineData("Api")]
    [InlineData("Coordinator")]
    public async Task Organisations_Create_AllowedWithViewAdminRights(string role)
    {
        var controller = BuildControllerWithRoles([role]);

        var result = await controller.Create(new CreateOrganisationDto("science"), _ct);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(OrganisationsController.GetById), createdResult.ActionName);

        _organisationRepositoryMock.Verify(r => r.Create(It.Is<Organisation>(o => o.Name == "science"), _ct),
            Times.Once);
    }

    [Fact]
    public async Task Organisations_GetById_ReturnsNotFound_WhenMissing()
    {
        _organisationRepositoryMock.Setup(r => r.GetById("missing", _ct))
            .ReturnsAsync((Organisation?)null);
        var controller = BuildControllerWithRoles(["Admin"]);

        var result = await controller.GetById("missing", _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task Organisations_Find_UsesDefaultLimit_WhenLimitIsNull()
    {
        _organisationRepositoryMock.Setup(r => r.Search("sci", 5, _ct))
            .ReturnsAsync([new Organisation { Id = "1", Name = "Science" }]);
        var controller = BuildControllerWithRoles(["Admin"]);

        var result = await controller.Find("sci", null, _ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dtos = Assert.IsAssignableFrom<IEnumerable<OrganisationDto>>(okResult.Value);
        Assert.Single(dtos);
        _organisationRepositoryMock.Verify(r => r.Search("sci", 5, _ct), Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(500, 100)]
    public async Task Organisations_Find_ClampsLimit(int requestedLimit, int expectedLimit)
    {
        _organisationRepositoryMock.Setup(r => r.Search("sci", expectedLimit, _ct))
            .ReturnsAsync([]);
        var controller = BuildControllerWithRoles(["Admin"]);

        _ = await controller.Find("sci", requestedLimit, _ct);

        _organisationRepositoryMock.Verify(r => r.Search("sci", expectedLimit, _ct), Times.Once);
    }

    private OrganisationsController BuildControllerWithRoles(string[] roles)
    {
        MockCurrentUser(roles);
        return new OrganisationsController(_organisationRepositoryMock.Object, _rightsService);
    }
}