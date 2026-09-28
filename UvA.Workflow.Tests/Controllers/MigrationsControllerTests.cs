using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UvA.Workflow.Api.Migrations;
using UvA.Workflow.Migrations;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.Users;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.Controllers;

public class MigrationsControllerTests
{
    [Fact]
    public async Task Get_ReturnsMigrationStatusAndTargets()
    {
        var migration = new Migration
        {
            MigrationId = "Project-Base:2026-09-09-rename-title",
            Scope = "Project-Base",
            Kind = MigrationKind.RenameProperty,
            Status = MigrationStatus.Failed,
            AttemptCount = 3,
            WorkflowDefinitions = ["Project"],
            OldProperty = "Title",
            NewProperty = "ProjectTitle",
            RequestedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([migration]);

        var result = await CreateController(repository).Get(CancellationToken.None);

        var migrations = Assert.IsAssignableFrom<IReadOnlyList<MigrationDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MigrationStatus.Failed, Assert.Single(migrations).Status);
        Assert.Equal(3, Assert.Single(migrations).AttemptCount);
        Assert.Equal("Project-Base", Assert.Single(migrations).Scope);
        Assert.Equal(["Project"], Assert.Single(migrations).WorkflowDefinitions);
    }

    [Fact]
    public async Task Get_WhenLoadingFails_ReturnsErrorMessageAndTraceId()
    {
        var repository = new Mock<IMigrationRepository>();
        const string message = "Requested value 'ReadyToFinish' was not found.";
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException(message));

        var result = await CreateController(repository).Get(CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, response.StatusCode);
        var body = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal("MigrationLoadFailed", body.GetProperty("error").GetString());
        Assert.Equal(message, body.GetProperty("message").GetString());
        Assert.Equal("migration-request", body.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Get_WithoutAdminRights_DoesNotReadMigrations()
    {
        var repository = new Mock<IMigrationRepository>();
        var controller = CreateController(repository, []);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.Get(CancellationToken.None));

        repository.Verify(value => value.GetAll(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static MigrationsController CreateController(Mock<IMigrationRepository> repository, string[]? roles = null)
    {
        var parser = UnitTestsHelpers.CreateModelParser();
        var modelService = new ModelService(parser);
        var migrationService = new MigrationService(modelService, repository.Object);
        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles ?? ["SystemAdmin"]);
        var rightsService = new RightsService(modelService, userService.Object,
            Mock.Of<IWorkflowInstanceRepository>());
        return new MigrationsController(migrationService, rightsService, NullLogger<MigrationsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "migration-request" }
            }
        };
    }
}