using UvA.Workflow.Api.Import.Dtos;
using UvA.Workflow.Api.Screens;

namespace UvA.Workflow.Api.Import;

using Infrastructure;
using UvA.Workflow.Import;

public class ImportController(
    IImportService importService,
    ModelService modelService,
    RightsService rightsService,
    InstanceAuthorizationFilterService authorizationFilterService)
    : ApiControllerBase
{
    private async Task<bool> CanImport(string workflowDefinition, CancellationToken ct)
    {
        // Global admin always allowed
        if (await rightsService.CanAny(workflowDefinition, RoleAction.ViewAdminTools))
            return true;

        // User level with edit rights on at least one instance is allowed
        return await authorizationFilterService.HasEditableInstances(workflowDefinition, ct);
    }

    [HttpGet("{workflowDefinition}/{screenName}/Columns")]
    public async Task<ActionResult<ImportablePropertyDto[]>> GetColumnNames(string workflowDefinition,
        string screenName,
        CancellationToken ct)
    {
        if (!await CanImport(workflowDefinition, ct))
            return Forbidden();

        var definition =
            modelService.WorkflowDefinitions.GetValueOrDefault(workflowDefinition);

        if (definition == null)
            return NotFound("DefinitionNotFound", $"Workflow definition '{workflowDefinition}' not found.");

        var screen = definition.Screens.FirstOrDefault(s => s.Name == screenName);
        if (screen == null)
            return NotFound("ScreenNotFound", $"Screen '{screenName}' not found for workflow '{workflowDefinition}'.");

        if (screen.BulkEdit is null)
            return BadRequest("BulkEditNotEnabled", $"Screen '{screenName}' does not support bulk edit.");

        var editableProperties =
            await importService.GetEditableImportableProperties(workflowDefinition, screen.BulkEdit.EditableProperties);
        var result = editableProperties
            .Select(p => new ImportablePropertyDto(p.Name, p.DisplayName, p.DataType))
            .ToArray();

        return Ok(result);
    }

    [HttpPost("{workflowDefinition}/{screenName}/Preview")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ImportPreview>> Preview(
        [FromForm] ImportPreviewRequest request, string workflowDefinition,
        string screenName, CancellationToken ct)
    {
        if (!await CanImport(workflowDefinition, ct))
            return Forbidden();

        await using var stream = request.File.OpenReadStream();

        var preview = await importService.PreviewAsync(
            workflowDefinition,
            screenName,
            stream,
            request.File.ContentType,
            request.Mappings,
            ct);

        return Ok(preview);
    }

    [HttpPost("{workflowDefinition}/{screenName}/Confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] ImportConfirmRequest request, string workflowDefinition,
        string screenName, CancellationToken ct)
    {
        if (!await CanImport(workflowDefinition, ct))
            return Forbidden();

        await importService.ImportAsync(
            workflowDefinition,
            screenName,
            request.Rows,
            ct);

        return Ok();
    }
}