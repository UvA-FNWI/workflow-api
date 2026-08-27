using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Authentication;
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
    public async Task Get_ReturnsHumanReadableMigrationProgress()
    {
        var migration = new Migration
        {
            Id = "migration-id",
            Kind = MigrationKind.RenameProperty,
            Status = MigrationStatus.ReadyToFinish,
            Definition = new RenamePropertyDefinition
            {
                WorkflowDefinitions = ["Project"],
                OldProperty = "Title",
                NewProperty = "ProjectTitle"
            },
            RequestedBy = "admin",
            RequestedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var repository = new Mock<IMigrationRepository>();
        repository.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([migration]);
        var parser = UnitTestsHelpers.CreateModelParser();
        var modelService = new ModelService(parser);
        var migrationService = new MigrationService(modelService, repository.Object);
        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["SystemAdmin"]);
        var rightsService = new RightsService(modelService, userService.Object,
            Mock.Of<IWorkflowInstanceRepository>());
        var controller = new MigrationsController(migrationService, rightsService,
            Mock.Of<ICurrentUserAccessor>());

        var result = await controller.Get(CancellationToken.None);

        var migrations = Assert.IsAssignableFrom<IReadOnlyList<MigrationDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Ready to finish", Assert.Single(migrations).StatusLabel);
    }
}