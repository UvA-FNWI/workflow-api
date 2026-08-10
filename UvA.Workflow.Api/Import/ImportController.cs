using UvA.Workflow.Api.Import.Dtos;

namespace UvA.Workflow.Api.Import;

using Infrastructure;
using UvA.Workflow.Import;

public class ImportController(ImportService importService, ModelService modelService, RightsService rightsService)
    : ApiControllerBase
{
    [HttpGet("Columns/{workflowDefinition}")]
    public async Task<ActionResult<ImportablePropertyDto[]>> GetColumnNames(string workflowDefinition,
        CancellationToken ct)
    {
        var definition =
            modelService.WorkflowDefinitions.GetValueOrDefault(workflowDefinition);

        if (definition == null)
            return NotFound("DefinitionNotFound", $"Workflow definition '{workflowDefinition}' not found.");

        var editActions = await rightsService.GetAllowedActions(definition.Name, RoleAction.Edit);

        var propertyNames = definition.Properties.Select(p => p.Name);

        var stub = new WorkflowInstance { WorkflowDefinition = workflowDefinition };

        var editablePropertiesMap = rightsService.CanEditProperties(stub, propertyNames, editActions);

        var result = definition.Properties
            .Where(p => editablePropertiesMap.GetValueOrDefault(p.Name) && importService.IsImportableType(p.DataType))
            .Select(p => new ImportablePropertyDto(p.Name, p.DisplayName, p.DataType))
            .ToArray();

        return Ok(result);
    }

    [HttpPost("Preview")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ImportPreview>> Preview(
        [FromForm] ImportPreviewRequest request, CancellationToken ct)
    {
        // await using var stream = request.File.OpenReadStream();
        //
        // var preview = await importService.PreviewAsync(
        //     stream,
        //     request.File.ContentType,
        //     request.WorkflowDefinition,
        //     request.Mappings,
        //     ct);
        var preview = request;

        return Ok(preview);
    }

    [HttpPost("Confirm")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Confirm(
        [FromForm] ImportPreviewRequest request, CancellationToken ct)
    {
        var response = request;
        // await using var stream = request.File.OpenReadStream();
        //
        // await importService.ImportAsync(
        //     stream,
        //     request.File.ContentType,
        //     request.WorkflowDefinition,
        //     request.Mappings,
        //     ct);

        return Ok();
    }
}