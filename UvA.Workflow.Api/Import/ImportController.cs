using UvA.Workflow.Api.Import.Dtos;

namespace UvA.Workflow.Api.Import;

using Infrastructure;
using UvA.Workflow.Import;

public class ImportController(ImportService importService) : ApiControllerBase
{
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