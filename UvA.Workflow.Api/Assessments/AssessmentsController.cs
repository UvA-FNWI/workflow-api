using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments;

public class AssessmentsController(
    SubmissionService submissionService,
    IUserService userService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService) : ApiControllerBase
{
    [HttpGet("{instanceId}/{submissionId}/Results")]
    public async Task<ActionResult<AssessmentGroupDto>> GetSubmissionResults(string instanceId, string submissionId,
        CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var contexts = await ResolveAssessmentContexts(instanceId, submissionId, ct);

        var dto = AssessmentGroupDto.Create(submissionId, contexts);
        return Ok(dto);
    }

    [HttpGet("{instanceId}/{submissionId}/Results/{pageName}")]
    public async Task<ActionResult<AssessmentGroupDto>> GetSubmissionResults(string instanceId, string submissionId,
        string pageName,
        CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var contexts = await ResolveAssessmentContexts(instanceId, submissionId, ct);

        var dto = AssessmentGroupDto.Create(submissionId, contexts, pageName);
        return Ok(dto);
    }

    private async Task<SubmissionContext[]> ResolveAssessmentContexts(
        string instanceId,
        string requestedId,
        CancellationToken ct)
    {
        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            throw new EntityNotFoundException("WorkflowInstance", instanceId);

        // 1. Exact form name? Return single form.
        var exactForm = modelService.GetFormOrDefault(instance, requestedId);
        if (exactForm != null)
        {
            return
            [
                await submissionService.GetSubmissionContext(instanceId, requestedId, null, ct)
            ];
        }

        // 2. Otherwise treat requestedId as actual/base form and resolve child forms
        var childForms = modelService.GetDerivedForms(instance, requestedId).ToArray();
        if (childForms.Length == 0)
            throw new EntityNotFoundException("Form", requestedId);

        return await Task.WhenAll(
            childForms.Select(f => submissionService.GetSubmissionContext(instanceId, f.Name, null, ct))
        );
    }
}