using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Import;
using UvA.Workflow.Api.Import.Dtos;
using UvA.Workflow.DocumentIO;
using UvA.Workflow.Import;
using UvA.Workflow.Submissions;
using UvA.Workflow.Tests.Controllers.Helpers;
using UvA.Workflow.WorkflowInstances;
using UvA.Workflow.WorkflowModel;

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
            new Mock<IExcelService>().Object,
            _workflowInstanceRepoMock.Object,
            answerConversionService,
            answerService,
            _modelService);
    }

    private ImportController BuildController(params string[] roles)
    {
        MockCurrentUser(roles);
        return new ImportController(_importService, _modelService, _rightsService);
    }

    private static ImportablePropertyDto[] GetProperties(ActionResult<ImportablePropertyDto[]> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<ImportablePropertyDto[]>(ok.Value);
    }

    private const string WorkflowDefinition = "Project";

    [Fact]
    public async Task GetColumnNames_ReturnsNotFound_WhenWorkflowDefinitionDoesNotExist()
    {
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames("NonExistent", _ct);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetColumnNames_Coordinator_ReturnsPropertiesFromEditableForm()
    {
        // Coordinator has Edit action on the Start form, which contains Title, Examiner, Reviewer,
        // Supervisor, StartDate, EndDate, EC - all of which are importable types.
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, _ct);
        var properties = GetProperties(result);

        Assert.Contains(properties, p => p.Name == "Title");
        Assert.Contains(properties, p => p.Name == "EC");
        Assert.Contains(properties, p => p.Name == "StartDate");
        Assert.Contains(properties, p => p.Name == "EndDate");
        Assert.Contains(properties, p => p.Name == "Examiner");
        Assert.Contains(properties, p => p.Name == "Supervisor");
        Assert.Contains(properties, p => p.Name == "Reviewer");
    }

    [Fact]
    public async Task GetColumnNames_Coordinator_ReturnsPropertyLevelEditableProperties()
    {
        // Coordinator also has property-level Edit rights on SecondReader and PracticalSupervisor.
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, _ct);
        var properties = GetProperties(result);

        Assert.Contains(properties, p => p.Name == "SecondReader");
        Assert.Contains(properties, p => p.Name == "PracticalSupervisor");
    }

    [Fact]
    public async Task GetColumnNames_Coordinator_ExcludesNonImportableTypes()
    {
        // Report is a File type and Course is a Reference — neither is importable.
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, _ct);
        var properties = GetProperties(result);

        Assert.DoesNotContain(properties, p => p.Name == "Report");
        Assert.DoesNotContain(properties, p => p.Name == "Course");
    }

    [Fact]
    public async Task GetColumnNames_Coordinator_ExcludesPropertiesNotInAnyEditableForm()
    {
        // TurnitinId is a String but is not part of any form the Coordinator can edit.
        // Student is a User but also not in the Start form.
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, _ct);
        var properties = GetProperties(result);

        Assert.DoesNotContain(properties, p => p.Name == "TurnitinId");
        Assert.DoesNotContain(properties, p => p.Name == "Student");
    }

    [Fact]
    public async Task GetColumnNames_Student_ReturnsOnlyPropertyLevelEditableProperties()
    {
        // Student has no form-level edit rights, only property-level rights
        // on SecondReader and PracticalSupervisor.
        var controller = BuildController("Student");

        var result = await controller.GetColumnNames(WorkflowDefinition, _ct);
        var properties = GetProperties(result);

        Assert.Equal(2, properties.Length);
        Assert.Contains(properties, p => p.Name == "SecondReader");
        Assert.Contains(properties, p => p.Name == "PracticalSupervisor");
    }

    [Fact]
    public async Task GetColumnNames_ReturnsEmpty_WhenUserHasNoEditRights()
    {
        // "Registered" role only has CreateInstance rights, no Edit rights.
        var controller = BuildController("Registered");

        var result = await controller.GetColumnNames(WorkflowDefinition, _ct);
        var properties = GetProperties(result);

        Assert.Empty(properties);
    }

    [Fact]
    public async Task GetColumnNames_ReturnsCorrectDataTypes()
    {
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, _ct);
        var properties = GetProperties(result);

        Assert.Equal(DataType.String, properties.Single(p => p.Name == "Title").DataType);
        Assert.Equal(DataType.Int, properties.Single(p => p.Name == "EC").DataType);
        Assert.Equal(DataType.Date, properties.Single(p => p.Name == "StartDate").DataType);
        Assert.Equal(DataType.User, properties.Single(p => p.Name == "SecondReader").DataType);
    }
}