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
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory) : ApiControllerBase
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
}