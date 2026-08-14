using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Migrations;
using UvA.Workflow.Migrations;
using UvA.Workflow.Persistence;
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
        var resolver = new ModelServiceResolver(new HttpContextAccessor());
        var parser = UnitTestsHelpers.CreateModelParser();
        resolver.AddOrUpdate("", parser, "layout", "active-sha", VersionKind.Baseline);
        var migration = new Migration
        {
            Id = "Project:RenameTitleToProjectTitle",
            Kind = MigrationKind.RenameProperty,
            WorkflowDefinition = "Project",
            OldPath = "Title",
            NewPath = "ProjectTitle",
            SourceCommit = "active-sha",
            TargetCommit = "target-sha",
            Stage = MigrationStage.SupportingBothNames,
            RunStatus = MigrationRunStatus.Waiting,
            RequestedBy = "admin",
            RequestedAt = DateTime.UtcNow
        };
        var store = new Mock<IMigrationStore>();
        store.Setup(value => value.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync([migration]);
        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetRolesOfCurrentUser(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["SystemAdmin"]);
        var rightsService = new RightsService(new ModelService(parser), userService.Object,
            Mock.Of<IWorkflowInstanceRepository>());
        var httpFactory = new Mock<IHttpClientFactory>();
        var settings = new Mock<ISettingsStore>();
        var loader = new WorkflowConfigLoader(httpFactory.Object, resolver, settings.Object,
            Options.Create(new WorkflowSourceOptions()), NullLogger<WorkflowConfigLoader>.Instance);
        var controller = new MigrationsController(resolver, loader, store.Object, rightsService,
            Mock.Of<ICurrentUserAccessor>());

        var result = await controller.Get(CancellationToken.None);

        var overview = Assert.IsType<MigrationOverviewDto>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        var returnedMigration = Assert.Single(overview.Migrations);
        Assert.Equal("Keeping old and new fields in sync", returnedMigration.StageLabel);
        Assert.Equal("Ready to copy existing data", returnedMigration.RunStatusLabel);
        Assert.Equal("active-sha", overview.ActiveConfiguration?.Commit);
        Assert.Null(overview.PendingConfiguration);
    }
}