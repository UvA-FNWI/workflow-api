using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Assessments;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments;

public class AssessmentsController(
    WorkflowInstanceService workflowInstanceService,
    IUserService userService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    IAssessmentService assessmentService,
    InstanceService instanceService,
    AssessmentDtoFactory assessmentDtoFactory,
    RightsService rightsService) : ApiControllerBase
{
    [HttpGet("{instanceId}")]
    public async Task<ActionResult<AssessmentDto>> GetAssessmentResults(string instanceId,
        CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            throw new EntityNotFoundException("WorkflowInstance", instanceId);

        await rightsService.EnsureAuthorizedForAction(instance, [RoleAction.View, RoleAction.ViewResults]);

        var assessmentConfig = await assessmentService.GetEnrichedAssessmentConfig(instance, ct);

        var submissions = (await instanceService.GetAllowedSubmissions(instance, ct, true)).ToList();
        var contexts = new List<SubmissionContext>();
        foreach (var form in submissions.Select(s => s.Form))
        {
            if (form == null)
                throw new EntityNotFoundException("Form", $"instanceId:{instanceId},submission:{form}");

            if (assessmentConfig?.Parts.SelectMany(p => p.Sources).Any(s => s.Name == form.Name) != true)
                continue;

            var context = await assessmentService.ResolveAssessmentContexts(instance, form.Name, ct);
            contexts.AddRange(context);
        }

        var dto = assessmentDtoFactory.Create(instance, contexts, assessmentConfig,
            allowedForms: submissions.Where(s => s.CanView).Select(s => s.Form.Name).ToArray());
        return Ok(dto);
    }

    [HttpGet("{instanceId}/{submissionId}")]
    public async Task<ActionResult<AssessmentDto>> GetSubmissionResults(string instanceId, string submissionId,
        CancellationToken ct, bool combine = false)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            throw new EntityNotFoundException("WorkflowInstance", instanceId);

        var assessmentConfig = await assessmentService.GetEnrichedAssessmentConfig(instance, ct);

        var matchingPart = combine
            ? assessmentConfig?.Parts.FirstOrDefault(p =>
                p.Name == submissionId || p.Sources.Any(s => s.Name == submissionId))
            : null;
        if (matchingPart != null)
        {
            await rightsService.EnsureAuthorizedForAction(instance, [RoleAction.View, RoleAction.ViewResults]);
            var allowedSubmissions = (await instanceService.GetAllowedSubmissions(instance, ct, true)).ToList();
            var contexts = await LoadSubmittedSourceContexts(instance, matchingPart, allowedSubmissions, ct);
            return Ok(assessmentDtoFactory.Create(instance, contexts,
                new AssessmentConfiguration { Parts = [matchingPart] },
                allowedForms: allowedSubmissions.Where(s => s.CanView).Select(s => s.Form.Name).ToArray()));
        }

        var context = await workflowInstanceService.GetSubmissionContext(instanceId, submissionId, null, ct);
        await rightsService.EnsureAuthorizedForAction(context.Instance, RoleAction.View, context.Form.Name);
        var dto = assessmentDtoFactory.Create(instance, [context], assessmentConfig);
        return Ok(dto);
    }

    /// <summary>
    /// Returns SubmissionContexts for all sources in the given part that have already
    /// been submitted. Sources not yet submitted are silently skipped
    /// </summary>
    private async Task<SubmissionContext[]> LoadSubmittedSourceContexts(
        WorkflowInstance instance,
        AssessmentPart part,
        IEnumerable<InstanceService.AllowedSubmission> allowedSubmissions,
        CancellationToken ct)
    {
        // Find out which forms have been submitted for this instance
        var submittedFormNames = allowedSubmissions.Select(s => s.Form.Name).ToHashSet();

        // Only fetch contexts for sources that have already been submitted
        var submittedSources = part.Sources
            .Where(source => submittedFormNames.Contains(source.Name))
            .ToList();

        return await Task.WhenAll(
            submittedSources.Select(source =>
                workflowInstanceService.GetSubmissionContext(instance.Id, source.Name, null, ct))
        );
    }

    [HttpGet("{instanceId}/{submissionId}/{pageName}")]
    public async Task<ActionResult<SourceResultDto>> GetSubmissionResults(string instanceId, string submissionId,
        string pageName,
        CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var context = await workflowInstanceService.GetSubmissionContext(instanceId, submissionId, null, ct);
        await rightsService.EnsureAuthorizedForAction(context.Instance, RoleAction.View, context.Form.Name);

        var dto = assessmentDtoFactory.CreateSourceResults(context, pageName);
        return Ok(dto);
    }
}