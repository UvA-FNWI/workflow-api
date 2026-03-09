using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Calculations.Dtos;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Calculations;

public class CalculationsController(
    SubmissionService submissionService) : ApiControllerBase
{
    [HttpGet("{instanceId}/{submissionId}")]
    public async Task<ActionResult<CalculationDto>> GetCalculations(string instanceId, string submissionId,
        CancellationToken ct)
    {
        var submissionContext = await submissionService.GetSubmissionContext(instanceId, submissionId, null, ct);
        var dto = CalculationDto.Create(submissionContext);
        return Ok(dto);
    }
}