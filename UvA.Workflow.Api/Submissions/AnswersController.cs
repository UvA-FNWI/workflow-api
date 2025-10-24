using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions;

public class SaveAnswerFileRequest
{
    [FromForm]
    public required IFormFile File { get; set; }
}

public class AnswersController(AnswerService answerService, RightsService rightsService, ArtifactTokenService artifactTokenService, SubmissionDtoFactory submissionDtoFactory) : ApiControllerBase
{
    [HttpPost("{instanceId}/{submissionId}/{questionName}")]
    public async Task<ActionResult<SaveAnswerResponse>> SaveAnswer(string instanceId, string submissionId,string questionName,
        [FromBody] AnswerInput input, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        await EnsureAuthorizedToEdit(context);
        var answers = await answerService.SaveAnswer(context, input.Value, ct);
        var updatedSubmission = submissionDtoFactory.Create(context.Instance,context.Form, context.Submission);
        return Ok(new SaveAnswerResponse(true, answers, updatedSubmission));
    }

    [HttpPost("{instanceId}/{submissionId}/{questionName}/artifacts")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    public async Task<ActionResult<SaveAnswerResponse>> SaveAnswerFile(string instanceId, string submissionId, string questionName, 
        [FromForm] SaveAnswerFileRequest request, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        await EnsureAuthorizedToEdit(context);
        await using var contents = request.File.OpenReadStream();
        await answerService.SaveArtifact(context, request.File.FileName, contents, ct);
        return Ok(new SaveAnswerFileResponse(true));
    }

    [HttpDelete("{instanceId}/{submissionId}/{questionName}/artifacts/{artifactId}")]
    public async Task<IActionResult> DeleteAnswerFile(string instanceId, string submissionId, string questionName, string artifactId, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        await EnsureAuthorizedToEdit(context);
        await answerService.DeleteArtifact(context, artifactId, ct);
        return Ok(new SaveAnswerFileResponse(true));
    }
    
    [HttpGet("{instanceId}/{submissionId}/{questionName}/artifacts/{artifactId}")]
    public async Task<IActionResult> GetAnswerFile(string instanceId, string submissionId, string questionName, 
        string artifactId,[FromQuery] string token, CancellationToken ct)
    {
        if (!await artifactTokenService.ValidateAccessToken(artifactId, token))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            return Unauthorized();
        }

        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        var file = await answerService.GetArtifact(context, artifactId, ct);
        if(file == null) return NotFound();
        return File(file.Content, "application/pdf", file.Info.Name);
    }

    private async Task EnsureAuthorizedToEdit(QuestionContext context)
    {
        var action = context.Submission?.Date == null ? RoleAction.Submit : RoleAction.Edit;
        if (!await rightsService.Can(context.Instance, action, context.Form.Name))
            throw new ForbiddenWorkflowActionException(context.Instance.Id, action, context.Form.Name);
    }
}