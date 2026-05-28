using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UvA.Workflow.Api.WorkflowDefinitions;
using UvA.Workflow.Api.WorkflowDefinitions.Dtos;
using UvA.Workflow.Tests.Controllers.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class WorkflowDefinitionsControllerTests : ControllerTestsBase
{
    [Fact]
    public async Task WorkflowDefinitions_GetAll_ReturnsAllowedActionsForCurrentUser()
    {
        MockCurrentUser("Admin");
        var controller = new WorkflowDefinitionsController(_modelService, _rightsService);

        var result = await controller.GetAll(_ct);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var definitions = Assert.IsAssignableFrom<IEnumerable<WorkflowDefinitionDto>>(okResult.Value).ToList();
        Assert.NotEmpty(definitions);
        Assert.All(definitions, d => Assert.NotNull(d.AllowedActions));
    }
}