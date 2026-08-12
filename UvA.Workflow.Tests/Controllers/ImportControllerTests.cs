using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Import;
using UvA.Workflow.Api.Import.Dtos;
using UvA.Workflow.Import;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests.Controllers;

public class ImportControllerTests : ControllerTestsBase
{
    private readonly ImportService _importService;

    public ImportControllerTests()
    {
        var answerConversionService = new AnswerConversionService(_userServiceMock.Object, _userRepoMock.Object);
        var answerService = new AnswerService(
            _modelService,
            _instanceService,
            _rightsService,
            _artifactServiceMock.Object,
            answerConversionService,
            _workflowInstanceService,
            _instanceEventService.Object,
            _instanceJournalServiceMock.Object,
            _userServiceMock.Object,
            _externalUserServiceMock.Object);

        _importService = new ImportService(
            [new Mock<IFileParserService>().Object],
            _workflowInstanceRepoMock.Object,
            answerConversionService,
            answerService,
            _modelService,
            _userRepoMock.Object,
            _rightsService);
    }

    private ImportController BuildController(params string[] roles)
    {
        MockCurrentUser(roles);
        return new ImportController(_importService, _modelService);
    }

    private static ImportablePropertyDto[] GetProperties(ActionResult<ImportablePropertyDto[]> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<ImportablePropertyDto[]>(ok.Value);
    }

    private const string WorkflowDefinition = "Project";
    private const string ScreenName = "Projects";

    [Fact]
    public async Task GetColumnNames_ReturnsNotFound_WhenWorkflowDefinitionDoesNotExist()
    {
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames("NonExistent", ScreenName, _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetColumnNames_ReturnsNotFound_WhenScreenNameDoesNotExist()
    {
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, "NonExistent", _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetColumnNames_Coordinator_ReturnsPropertiesFromEditableForm()
    {
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, ScreenName, _ct);
        var properties = GetProperties(result);

        Assert.Contains(properties, p => p.Name == "Title");
        Assert.Contains(properties, p => p.Name == "EC");
        Assert.Contains(properties, p => p.Name == "StartDate");
        Assert.Contains(properties, p => p.Name == "EndDate");
        Assert.Contains(properties, p => p.Name == "Supervisor");
    }

    [Fact]
    public async Task GetColumnNames_Coordinator_ExcludesNonImportableTypes()
    {
        // Report is a File type and Course is a Reference — neither is importable.
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, ScreenName, _ct);
        var properties = GetProperties(result);

        Assert.DoesNotContain(properties, p => p.Name == "Report");
        Assert.DoesNotContain(properties, p => p.Name == "Course");
    }

    [Fact]
    public async Task GetColumnNames_ReturnsEmpty_WhenUserHasNoEditRights()
    {
        // "Registered" role only has CreateInstance rights, no Edit rights.
        var controller = BuildController("Registered");

        var result = await controller.GetColumnNames(WorkflowDefinition, ScreenName, _ct);
        var properties = GetProperties(result);

        Assert.Empty(properties);
    }

    [Fact]
    public async Task GetColumnNames_ReturnsCorrectDataTypes()
    {
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, ScreenName, _ct);
        var properties = GetProperties(result);

        Assert.Equal(DataType.String, properties.Single(p => p.Name == "Title").DataType);
        Assert.Equal(DataType.Int, properties.Single(p => p.Name == "EC").DataType);
        Assert.Equal(DataType.Date, properties.Single(p => p.Name == "StartDate").DataType);
        Assert.Equal(DataType.User, properties.Single(p => p.Name == "SecondReader").DataType);
    }
}