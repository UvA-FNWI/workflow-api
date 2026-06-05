using Microsoft.AspNetCore.Mvc;
using UvA.Workflow.Api.WorkflowDefinitions;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class WorkflowDefinitionsControllerTests : ControllerTestsBase
{
    private WorkflowDefinitionsController BuildController(params string[] roles)
    {
        MockCurrentUser(roles);
        return new WorkflowDefinitionsController(_modelService, _rightsService);
    }

    private static List<WorkflowDefinitionDto> GetDtos(ActionResult<IEnumerable<WorkflowDefinitionDto>> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<IEnumerable<WorkflowDefinitionDto>>(ok.Value).ToList();
    }

    [Fact]
    public async Task GetAll_Default_OnlyReturnsDefinitionsWithScreens()
    {
        var controller = BuildController("Coordinator");

        var dtos = GetDtos(await controller.GetAll());

        Assert.NotEmpty(dtos);
        Assert.All(dtos, d => Assert.NotEmpty(d.Screens));
        // "Assessment" has no screens, so it must be excluded by default
        Assert.DoesNotContain(dtos, d => d.Name == "Assessment");
    }

    [Fact]
    public async Task GetAll_IncludeAll_IncludesDefinitionsWithoutScreens()
    {
        var controller = BuildController("Coordinator");

        var defaultDtos = GetDtos(await controller.GetAll());
        var allDtos = GetDtos(await controller.GetAll(includeAll: true));

        Assert.True(allDtos.Count > defaultDtos.Count);
        Assert.Contains(allDtos, d => d.Screens.Length == 0);
        Assert.Contains(allDtos, d => d.Name == "Assessment");
    }

    [Fact]
    public async Task GetAll_SetsCanCreateInstance_ForDefinitionsTheUserCanCreate()
    {
        // The "Project" definition grants CreateInstance to the always-present "Registered" role.
        var controller = BuildController();

        var dtos = GetDtos(await controller.GetAll(includeAll: true));

        var project = Assert.Single(dtos, d => d.Name == "Project");
        Assert.True(project.CanCreateInstance);

        // "Context" is only creatable by the "Api" role, which this user does not have.
        var context = Assert.Single(dtos, d => d.Name == "Context");
        Assert.False(context.CanCreateInstance);
    }

    [Fact]
    public async Task GetAll_CanCreateInstance_RespectsRole()
    {
        // The "Api" role grants CreateInstance for "Context".
        var controller = BuildController("Api");

        var dtos = GetDtos(await controller.GetAll(includeAll: true));

        var context = Assert.Single(dtos, d => d.Name == "Context");
        Assert.True(context.CanCreateInstance);
    }
}