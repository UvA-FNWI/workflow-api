using System.Text.Json;
using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Submissions;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Submissions;

public class SubmissionsController(
    IUserService userService,
    ModelService modelService,
    RightsService rightsService,
    SubmissionService submissionService,
    WorkflowInstanceService workflowInstanceService,
    SubmissionDtoFactory submissionDtoFactory,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory,
    AnswerService answerService,
    DummyAnswerGenerator dummyAnswerGenerator) : ApiControllerBase
{
    [HttpGet("{instanceId}/{submissionId}")]
    public async Task<ActionResult<SubmissionDto>> GetSubmission(string instanceId, string submissionId,
        [FromQuery] int? version = null,
        CancellationToken ct = default)
    {
        var (instance, submissionState, form, _) =
            await workflowInstanceService.GetSubmissionContext(instanceId, submissionId, version, ct);

        // If the form is not yet submitted, you can view it with submit permissions. After that, view permissions apply
        await rightsService.EnsureAuthorizedForAction(instance,
            submissionState.DateSubmitted == null ? RoleAction.Submit : RoleAction.View, form.Name);

        var permissions =
            await rightsService.GetAllowedActionsForForm(instance, form, RoleAction.ViewAdminTools, RoleAction.Edit);
        var dto = submissionDtoFactory.Create(instance, form, submissionState,
            modelService.GetQuestionStatus(instance, form, true), permissions.Select(p => p.Type).ToArray());
        return Ok(dto);
    }

    [HttpPost("{instanceId}/{submissionId}")]
    public async Task<ActionResult<SubmitSubmissionResult>> SubmitSubmission(string instanceId, string submissionId,
        CancellationToken ct)
    {
        var user = await userService.GetCurrentUser(ct);
        if (user == null)
            return Unauthorized();

        var context = await workflowInstanceService.GetSubmissionContext(instanceId, submissionId, null, ct);
        var (instance, _, form, _) = context;

        await rightsService.EnsureAuthorizedForAction(instance, RoleAction.Submit, form.Name);
        var permissions =
            await rightsService.GetAllowedActionsForForm(instance, form, RoleAction.ViewAdminTools, RoleAction.Edit);

        var result = await submissionService.SubmitSubmission(context, user, ct);

        if (!result.Success)
        {
            var submissionDto = submissionDtoFactory.Create(instance, form, result.SubmissionState,
                modelService.GetQuestionStatus(instance, form, true), permissions.Select(p => p.Type).ToArray());

            return UnprocessableEntity(new SubmitSubmissionResult(submissionDto, null, result.Errors, false));
        }

        var finalSubmissionDto = submissionDtoFactory.Create(instance, form, result.SubmissionState,
            modelService.GetQuestionStatus(instance, form, true), permissions.Select(p => p.Type).ToArray());
        var updatedInstanceDto = await workflowInstanceDtoFactory.Create(instance, ct);

        return Ok(new SubmitSubmissionResult(finalSubmissionDto, updatedInstanceDto,
            EffectResult: result.EffectResult));
    }

    [HttpPost("{instanceId}/{submissionId}/dummyData")]
    public async Task<ActionResult<SubmissionDto>> GenerateDummyData(string instanceId, string submissionId,
        CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ImpersonateRoles);

        var (instance, submissionState, form, _) =
            await workflowInstanceService.GetSubmissionContext(instanceId, submissionId, null, ct);

        // The do/while loop is needed for re-evaluating the question status since filling in questions can trigger
        // dependent question to become visible
        var filledQuestionIds = new HashSet<string>();
        bool anyNewVisibleQuestions;
        var lastGeneratedDate = DateTime.Now;

        do
        {
            anyNewVisibleQuestions = false;
            var questionStatus = modelService.GetQuestionStatus(instance, form, canViewHidden: true);

            foreach (var (questionName, status) in questionStatus.Where(q => q.Value.IsVisible))
            {
                if (filledQuestionIds.Contains(questionName)) continue;

                var question = modelService.GetQuestion(instance, form.PropertyName, questionName);
                if (question == null) continue;

                var existingAnswer = instance.GetProperty(form.PropertyName, questionName);
                if (existingAnswer != null && !existingAnswer.IsBsonNull && existingAnswer != string.Empty) continue;

                if (question.DataType == DataType.User)
                {
                    var currentUser = await userService.GetCurrentUser(ct);
                    if (currentUser != null)
                    {
                        var userObject = new
                        {
                            userName = currentUser.UserName,
                            displayName = currentUser.DisplayName,
                            email = currentUser.Email
                        };
                        var userElement = question.IsArray
                            ? JsonSerializer.SerializeToElement(new[] { userObject })
                            : JsonSerializer.SerializeToElement(userObject);
                        await answerService.SaveAnswer(
                            new QuestionContext(instance, submissionState, form, question), userElement, ct);
                        filledQuestionIds.Add(questionName);
                        anyNewVisibleQuestions = true;
                    }

                    continue;
                }

                var dummyAnswer = dummyAnswerGenerator.Generate(question, status, lastGeneratedDate);

                if (dummyAnswer == null) continue;

                if (question.DataType is DataType.DateTime or DataType.Date
                    && dummyAnswer.Value.GetString() is { } dateStr
                    && DateTime.TryParse(dateStr, out var parsedDate))
                {
                    lastGeneratedDate = parsedDate;
                }

                await answerService.SaveAnswer(
                    new QuestionContext(instance, submissionState, form, question), dummyAnswer, ct);

                filledQuestionIds.Add(questionName);
                anyNewVisibleQuestions = true;
            }
        } while (anyNewVisibleQuestions);

        var permissions =
            await rightsService.GetAllowedActionsForForm(instance, form, RoleAction.ViewAdminTools, RoleAction.Edit);
        var updatedSubmission = submissionDtoFactory.Create(instance, form, submissionState,
            modelService.GetQuestionStatus(instance, form, true),
            permissions.Select(p => p.Type).ToArray());

        return Ok(updatedSubmission);
    }
}