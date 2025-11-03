using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Submissions.Dtos;
using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Submissions;

public class SubmissionsController(
    ModelService modelService,
    SubmissionService submissionService,
    SubmissionDtoFactory submissionDtoFactory,
    WorkflowInstanceDtoFactory workflowInstanceDtoFactory) : ApiControllerBase
{
    [HttpGet("{instanceId}/{submissionId}")]
    public async Task<ActionResult<SubmissionDto>> GetSubmission(string instanceId, string submissionId,
        CancellationToken ct)
    {
        var (instance, submission, form, _) =
            await submissionService.GetSubmissionContext(instanceId, submissionId, ct);
        var dto = submissionDtoFactory.Create(instance, form, submission,
            modelService.GetQuestionStatus(instance, form, true));
        return Ok(dto);
    }

    [HttpPost("{instanceId}/{submissionId}")]
    public async Task<ActionResult<SubmitSubmissionResult>> SubmitSubmission(string instanceId, string submissionId,
        CancellationToken ct)
    {
        var context = await submissionService.GetSubmissionContext(instanceId, submissionId, ct);
        var (instance, sub, form, _) = context;
        var result = await submissionService.SubmitSubmission(context, ct);

        if (!result.Success)
        {
            var submissionDto = submissionDtoFactory.Create(instance, form, sub,
                modelService.GetQuestionStatus(instance, form, true));
            return Ok(new SubmitSubmissionResult(submissionDto, null, result.Errors, false));
        }

        var finalSubmissionDto = submissionDtoFactory.Create(instance, form, instance.Events[submissionId],
            modelService.GetQuestionStatus(instance, form, true));
        var updatedInstanceDto = await workflowInstanceDtoFactory.Create(instance, ct);

        return Ok(new SubmitSubmissionResult(finalSubmissionDto, updatedInstanceDto));
    }
}