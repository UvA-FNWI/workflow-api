using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions;

public class AnswersController(
    IUserService userService,
    AnswerService answerService,
    RightsService rightsService,
    ArtifactTokenService artifactTokenService,
    SubmissionDtoFactory submissionDtoFactory,
    InstanceService instanceService,
    ModelService modelService,
    IWorkflowInstanceRepository workflowInstanceRepository) : ApiControllerBase
{
    [HttpPost("{instanceId}/{submissionId}/{questionName}")]
    public async Task<ActionResult<SaveAnswerResponse>> SaveAnswer(string instanceId, string submissionId,
        string questionName,
        [FromBody] AnswerInput input, CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null) return Unauthorized();
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        await EnsureAuthorizedToEdit(context);
        var answers = await answerService.SaveAnswer(context, input.Value, user, ct);
        var updatedSubmission = submissionDtoFactory.Create(context.Instance, context.Form, context.Submission);
        return Ok(new SaveAnswerResponse(true, answers, updatedSubmission));
    }

    [HttpPost("{instanceId}/{submissionId}/{questionName}/artifacts")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    public async Task<ActionResult<SaveAnswerResponse>> SaveAnswerFile(string instanceId, string submissionId,
        string questionName,
        [FromForm] SaveAnswerFileRequest request, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        await EnsureAuthorizedToEdit(context);
        await using var contents = request.File.OpenReadStream();
        await answerService.SaveArtifact(context, request.File.FileName, contents, ct);
        return Ok(new SaveAnswerFileResponse(true));
    }

    [HttpDelete("{instanceId}/{submissionId}/{questionName}/artifacts/{artifactId}")]
    public async Task<IActionResult> DeleteAnswerFile(string instanceId, string submissionId, string questionName,
        string artifactId, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        await EnsureAuthorizedToEdit(context);
        await answerService.DeleteArtifact(context, artifactId, ct);
        return Ok(new SaveAnswerFileResponse(true));
    }

    [HttpGet("{instanceId}/{submissionId}/{questionName}/artifacts/{artifactId}")]
    public async Task<IActionResult> GetAnswerFile(string instanceId, string submissionId, string questionName,
        string artifactId, [FromQuery] string token, CancellationToken ct)
    {
        if (!await artifactTokenService.ValidateAccessToken(artifactId, token))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            return Unauthorized();
        }

        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        var file = await answerService.GetArtifact(context, artifactId, ct);
        if (file == null) return NotFound();
        return File(file.Content, "application/pdf", file.Info.Name);
    }

    [HttpGet("{instanceId}/{submissionId}/{questionName}/Choices")]
    public async Task<ActionResult<IEnumerable<ChoiceDto>>> GetChoices(string instanceId, string submissionId,
        string questionName, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        var insts = await instanceService.GetPossibleChoices(context.Instance, context.PropertyDefinition, ct);
        var definition = context.PropertyDefinition.WorkflowDefinition!;
        return Ok(insts.Select(i => new ChoiceDto(
            i.Id,
            definition.InstanceTitleTemplate?.Execute(modelService.CreateContext(i)) ?? "nameless",
            null))
        );
    }

    [HttpGet("{instanceId}/{submissionId}/{questionName}/CurrentChoices")]
    public async Task<ActionResult<IEnumerable<ChoiceDto>>> GetCurrentChoices(string instanceId, string submissionId,
        string questionName, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        var value = modelService.CreateContext(context.Instance).Get(questionName);
        var ids = value switch
        {
            string s => [s],
            string[] ss => ss,
            _ => []
        };

        if (ids.Length == 0 || context.PropertyDefinition.DataType != DataType.Reference)
            return NotFound();

        var definition = context.PropertyDefinition.WorkflowDefinition!;
        var insts = await workflowInstanceRepository.GetByIds(ids, ct);
        return Ok(insts.Select(i => new ChoiceDto(
            i.Id,
            definition.InstanceTitleTemplate?.Execute(modelService.CreateContext(i)) ?? "",
            null)
        ));
    }

    private async Task EnsureAuthorizedToEdit(QuestionContext context)
    {
        var action = context.Submission?.Date == null ? RoleAction.Submit : RoleAction.Edit;
        if (!await rightsService.Can(context.Instance, action, context.Form.Name))
            throw new ForbiddenWorkflowActionException(context.Instance.Id, action, context.Form.Name);
    }
}