using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions;

public class FormPreviewsController(
    IWorkflowInstanceRepository repository,
    IUserService userService,
    FormPreviewService previewService) : ApiControllerBase
{
    [HttpPost("{instanceId}/{formName}")]
    public async Task<ActionResult<FormPreviewDto>> Evaluate(string instanceId, string formName,
        [FromBody] FormPreviewRequest input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (await userService.GetCurrentUser(ct) == null)
            return Unauthorized();
        var instance = await repository.GetById(instanceId, ct);
        if (instance == null)
            return WorkflowInstanceNotFound;
        if (input.Answers == null)
            return BadRequest("InvalidInput", "Temporary answers are required.");
        return Ok(FormPreviewDto.Create(await previewService.Evaluate(instance, formName, input.Answers, ct)));
    }
}