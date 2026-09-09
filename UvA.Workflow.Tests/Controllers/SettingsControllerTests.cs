using Microsoft.AspNetCore.Mvc;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Settings;
using UvA.Workflow.Api.Settings.Dtos;
using UvA.Workflow.Tests.Helpers;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Tests.Controllers;

public class SettingsControllerTests
{
    [Fact]
    public void Get_ReturnsNotFound_WhenSettingsYamlIsMissing()
    {
        var controller = new SettingsController(
            new ResolvedWorkflowConfig(new ModelService(UnitTestsHelpers.CreateModelParser()), "layout", null));

        var result = controller.Get();

        var notFound = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public void Get_ReturnsUiOverviewCopy_WhenSettingsAreAvailable()
    {
        var settings = new WorkflowSettings
        {
            Ui = new UiSettings
            {
                Overview = new OverviewUiSettings
                {
                    Title = new BilingualString("Registers", "Registers"),
                    NoAccess = new BilingualString("No access", "Geen toegang")
                }
            }
        };
        var controller = new SettingsController(
            new ResolvedWorkflowConfig(new ModelService(UnitTestsHelpers.CreateModelParser()), "layout", settings));

        var result = controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<SettingsDto>(ok.Value);
        Assert.Equal("Registers", dto.Ui!.Overview!.Title!.En);
        Assert.Equal("No access", dto.Ui.Overview.NoAccess!.En);
    }
}