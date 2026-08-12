using UvA.Workflow.Api.Import.Dtos;

namespace UvA.Workflow.Api.Import;

using Infrastructure;
using UvA.Workflow.Import;

public class ImportController(ImportService importService, ModelService modelService)
    : ApiControllerBase
{
    [HttpGet("Columns/{workflowDefinition}/{screenName}")]
    public async Task<ActionResult<ImportablePropertyDto[]>> GetColumnNames(string workflowDefinition,
        string screenName,
        CancellationToken ct)
    {
        var definition =
            modelService.WorkflowDefinitions.GetValueOrDefault(workflowDefinition);

        if (definition == null)
            return NotFound("DefinitionNotFound", $"Workflow definition '{workflowDefinition}' not found.");

        var screen = definition.Screens.FirstOrDefault(s => s.Name == screenName);
        if (screen == null)
            return NotFound("ScreenNotFound", $"Screen '{screenName}' not found for workflow '{workflowDefinition}'.");

        if (screen.BulkEditProperties is not { Length: > 0 })
            return BadRequest("BulkEditNotEnabled", $"Screen '{screenName}' does not support bulk edit.");

        var editableProperties =
            await importService.GetEditableImportableProperties(workflowDefinition, screen.BulkEditProperties);
        var result = editableProperties
            .Select(p => new ImportablePropertyDto(p.Name, p.DisplayName, p.DataType))
            .ToArray();

        return Ok(result);
    }

    [HttpPost("Preview")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ImportPreview>> Preview(
        [FromForm] ImportPreviewRequest request, CancellationToken ct)
    {
        await using var stream = request.File.OpenReadStream();

        var preview = await importService.PreviewAsync(
            stream,
            request.File.ContentType,
            request.WorkflowDefinition,
            request.Mappings,
            ct);

        return Ok(preview);
    }

    [HttpPost("Confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] ImportConfirmRequest request, CancellationToken ct)
    {
        await importService.ImportAsync(
            request.WorkflowDefinition,
            request.Rows,
            ct);

        return Ok();
    }
}