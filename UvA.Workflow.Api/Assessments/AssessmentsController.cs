using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments;

public class AssessmentsController(
    SubmissionService submissionService) : ApiControllerBase
{
    [HttpGet("{instanceId}/{submissionId}/Results")]
    public async Task<ActionResult<AssessmentDto>> GetSubmissionResults(string instanceId, string submissionId,
        CancellationToken ct)
    {
        var submissionContext = await submissionService.GetSubmissionContext(instanceId, submissionId, null, ct);
        var dto = AssessmentDto.Create(submissionContext);
        return Ok(dto);
    }

    [HttpGet("{instanceId}/{submissionId}/Results/{pageName}")]
    public async Task<ActionResult<AssessmentPageDto>> GetSubmissionResults(string instanceId, string submissionId,
        string pageName,
        CancellationToken ct)
    {
        var submissionContext = await submissionService.GetSubmissionContext(instanceId, submissionId, null, ct);
        var dto = AssessmentPageDto.Create(submissionContext, pageName);
        return Ok(dto);
    }
}