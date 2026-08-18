using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using UvA.Workflow.Api.Import;
using UvA.Workflow.Api.Import.Dtos;
using UvA.Workflow.Import;
using UvA.Workflow.Tests.Controllers.Helpers;

namespace UvA.Workflow.Tests.Controllers;

public class ImportControllerTests : ControllerTestsBase
{
    private readonly Mock<IImportService> _importServiceMock = new();
    private readonly ImportPreview _fakePreview = new([], []);

    private ImportController BuildController(params string[] roles)
    {
        MockCurrentUser(roles);
        _importServiceMock
            .Setup(s => s.PreviewAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<ColumnMapping[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_fakePreview);
        return new ImportController(_importServiceMock.Object, _modelService, _rightsService);
    }

    private static ImportablePropertyDto[] GetProperties(ActionResult<ImportablePropertyDto[]> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<ImportablePropertyDto[]>(ok.Value);
    }

    private static IFormFile MakeCsvFile(string csv)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "import.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
    }

    private static string MappingJson(params (string excel, string prop)[] cols) =>
        "[" + string.Join(",", cols.Select(c =>
            $"{{\"excelColumn\":\"{c.excel}\",\"propertyName\":\"{c.prop}\"}}")) + "]";

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
    public async Task GetColumnNames_Throws_WhenUserHasNoEditRights()
    {
        // "Registered" role only has CreateInstance rights, no Edit rights.
        var controller = BuildController("Registered");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.GetColumnNames(WorkflowDefinition, ScreenName, _ct));
    }

    [Fact]
    public async Task GetColumnNames_ReturnsOk_WithServiceResult()
    {
        var fakeProperties = new PropertyDefinition[]
        {
            /* minimal stub */
        };
        _importServiceMock
            .Setup(s => s.GetEditableImportableProperties(WorkflowDefinition, It.IsAny<string[]>()))
            .ReturnsAsync(fakeProperties);
        var controller = BuildController("Coordinator");

        var result = await controller.GetColumnNames(WorkflowDefinition, ScreenName, _ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<ImportablePropertyDto[]>(ok.Value);
    }


    [Fact]
    public async Task Preview_Throws_WhenUserHasNoEditRights()
    {
        var controller = BuildController("Registered");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.Preview(new ImportPreviewRequest
            {
                File = new FormFile(new MemoryStream(), 0, 0, "file", "test.csv"),
                ColumnMapping = "[]"
            }, WorkflowDefinition, ScreenName, _ct));
    }

    [Fact]
    public async Task Preview_ReturnsOk_WithServiceResult()
    {
        var controller = BuildController("Coordinator");

        var result = await controller.Preview(
            new ImportPreviewRequest
            {
                File = MakeCsvFile("StudentNumber\n"),
                ColumnMapping = MappingJson(("StudentNumber", "Student.UserName"))
            },
            WorkflowDefinition, ScreenName, _ct);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(_fakePreview, ok.Value);
    }

    [Fact]
    public async Task Confirm_Throws_WhenUserHasNoEditRights()
    {
        var controller = BuildController("Registered");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.Confirm(
                new ImportConfirmRequest([]),
                WorkflowDefinition, ScreenName, _ct));
    }

    [Fact]
    public async Task Confirm_ReturnsOk_WithEmptyRowList()
    {
        var controller = BuildController("Coordinator");

        var result = await controller.Confirm(
            new ImportConfirmRequest([]),
            WorkflowDefinition, ScreenName, _ct);

        Assert.IsType<OkResult>(result);
    }
}