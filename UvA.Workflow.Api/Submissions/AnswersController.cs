using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.Users.Dtos;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions;

public class AnswersController(
    AnswerService answerService,
    AnswerConversionService answerConversionService,
    RightsService rightsService,
    IExternalUserService externalUserService,
    ArtifactTokenService artifactTokenService,
    SubmissionDtoFactory submissionDtoFactory,
    InstanceService instanceService,
    ModelService modelService,
    IWorkflowInstanceRepository workflowInstanceRepository) : ApiControllerBase
{
    private const string ManualUserInternalEmailCode = "ManualUserInternalEmail";
    private const string ManualUserEmailAlreadyExistsCode = "ManualUserEmailAlreadyExists";
    private const string InvalidEmailAddressCode = "InvalidEmailAddress";
    private const string InvalidQuestionTypeCode = "InvalidQuestionType";
    private const string ExternalUsersNotAllowedCode = "ExternalUsersNotAllowed";
    private const string InvalidChoiceValueCode = "InvalidChoiceValue";

    [HttpPost("{instanceId}/{submissionId}/{questionName}")]
    public async Task<ActionResult<SaveAnswerResponse>> SaveAnswer(string instanceId, string submissionId,
        string questionName,
        [FromBody] SaveAnswerRequest input, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        await EnsureAuthorizedToEdit(context);

        var value = input.Value;
        UserSearchResultDto? createdUser = null;
        if (input.ExternalUser != null)
        {
            if (context.PropertyDefinition.DataType != DataType.User)
                return Unprocessable(InvalidQuestionTypeCode, InvalidQuestionTypeCode);

            if (context.PropertyDefinition.AllowsExternalUsers != true)
                return Unprocessable(ExternalUsersNotAllowedCode, ExternalUsersNotAllowedCode);

            try
            {
                var externalUser = await externalUserService.CreateOrUpdateExternalUser(
                    input.ExternalUser.DisplayName,
                    input.ExternalUser.Email,
                    input.ExternalUser.Organization,
                    ct);
                createdUser = UserSearchResultDto.Create(externalUser);
                value = JsonSerializer.SerializeToElement(createdUser, AnswerConversionService.Options);
            }
            catch (ExternalUserCreationException ex)
            {
                return MapExternalUserCreationError(ex);
            }
        }

        if (context.PropertyDefinition.DataType == DataType.User &&
            context.PropertyDefinition.AllowsExternalUsers != true &&
            value is JsonElement userValue &&
            await answerConversionService.ContainsExternalUserSelection(userValue, context.PropertyDefinition.IsArray,
                ct))
            return Unprocessable(ExternalUsersNotAllowedCode, ExternalUsersNotAllowedCode);

        if (context.PropertyDefinition.DataType == DataType.Choice && value is JsonElement choiceValue &&
            AnswerConversionService.FindInvalidChoice(choiceValue, context.PropertyDefinition) is { } invalidChoice)
            return Unprocessable(InvalidChoiceValueCode,
                $"'{invalidChoice}' is not a valid value for '{questionName}'");

        var answers = await answerService.SaveAnswer(context, value, ct);
        var permissions =
            await rightsService.GetAllowedActionsForForm(context.Instance, context.Form, RoleAction.ViewAdminTools,
                RoleAction.Edit);
        var updatedSubmission = submissionDtoFactory.Create(context.Instance, context.Form, context.SubmissionState,
            modelService.GetQuestionStatus(context.Instance, context.Form, true),
            permissions.Select(p => p.Type).ToArray());
        return Ok(new SaveAnswerResponse(true, answers, updatedSubmission, User: createdUser));
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

        await answerService.SaveArtifact(context, request.File, ct);
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

    [AllowAnonymous]
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

        return File(file.Content, file.Info.ContentType, file.Info.Name);
    }

    [HttpGet("{instanceId}/{submissionId}/{questionName}/Choices")]
    public async Task<ActionResult<IEnumerable<ChoiceDto>>> GetChoices(string instanceId, string submissionId,
        string questionName, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        await EnsureAuthorizedForAction(context, RoleAction.View);

        var insts = await instanceService.GetPossibleChoices(context.Instance, context.PropertyDefinition, ct);
        var definition = context.PropertyDefinition.WorkflowDefinition!;
        return Ok(insts.Select(i => new ChoiceDto(
            i.Id,
            definition.InstanceTitleTemplate?.Execute(modelService.CreateContext(i)) ?? "nameless",
            null,
            null))
        );
    }

    [HttpGet("{instanceId}/{submissionId}/{questionName}/CurrentChoices")]
    public async Task<ActionResult<IEnumerable<ChoiceDto>>> GetCurrentChoices(string instanceId, string submissionId,
        string questionName, CancellationToken ct)
    {
        var context = await answerService.GetQuestionContext(instanceId, submissionId, questionName, ct);
        await EnsureAuthorizedForAction(context, RoleAction.View);

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
            null,
            null)
        ));
    }

    private async Task EnsureAuthorizedToEdit(QuestionContext context) =>
        await EnsureAuthorizedForAction(context,
            context.SubmissionState.IsSubmitted ? RoleAction.Edit : RoleAction.Submit);

    private ObjectResult MapExternalUserCreationError(ExternalUserCreationException ex) => ex.Reason switch
    {
        ExternalUserCreationFailureReason.InvalidEmailAddress =>
            BadRequest(InvalidEmailAddressCode, InvalidEmailAddressCode),
        ExternalUserCreationFailureReason.InternalEmailAddress =>
            BadRequest(ManualUserInternalEmailCode, ManualUserInternalEmailCode),
        ExternalUserCreationFailureReason.UserAlreadyExists =>
            Conflict(ManualUserEmailAlreadyExistsCode, ManualUserEmailAlreadyExistsCode),
        _ => Unprocessable(ex.Reason.ToString(), ex.Message)
    };

    private async Task EnsureAuthorizedForAction(QuestionContext context, RoleAction action) =>
        await rightsService.EnsureAuthorizedForAction(context.Instance, [action], RightsEvaluationMode.RequestContext,
            context.Form.Name);
}