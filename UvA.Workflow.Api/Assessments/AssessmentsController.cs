using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Assessments.Dtos;
using UvA.Workflow.Infrastructure;
using UvA.Workflow.Submissions;

namespace UvA.Workflow.Api.Assessments;

public class AssessmentsController(
    SubmissionService submissionService,
    IUserService userService,
    IWorkflowInstanceRepository workflowInstanceRepository,
    ModelService modelService,
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

        var assessmentConfig = modelService.WorkflowDefinitions[instance.WorkflowDefinition].AssessmentConfiguration;

        await rightsService.EnsureAuthorizedForAction(instance, RoleAction.View);
        var submissions = await instanceService.GetAllowedSubmissions(instance, ct);

        var contexts = new List<SubmissionContext>();
        foreach (var form in submissions.Select(s => s.Form))
        {
            if (form == null)
                throw new EntityNotFoundException("Form", $"instanceId:{instanceId},submission:{form}");

            if (assessmentConfig?.Parts.SelectMany(p => p.Sources).Any(s => s.Name == form.Name) != true)
                continue;

            var (context, _) = await ResolveAssessmentContexts(instanceId, form.Name, ct);
            contexts.AddRange(context);
        }

        var dto = assessmentDtoFactory.Create(instanceId, contexts, assessmentConfig);
        return Ok(dto);
    }

    [HttpGet("{instanceId}/{submissionId}/Results")]
    public async Task<ActionResult<AssessmentDto>> GetSubmissionResults(string instanceId, string submissionId,
        CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var (contexts, assessmentConfig) = await ResolveAssessmentContexts(instanceId, submissionId, ct);

        await Task.WhenAll(contexts.Select(context =>
            rightsService.EnsureAuthorizedForAction(context.Instance, RoleAction.View, context.Form.Name)));

        var dto = assessmentDtoFactory.Create(submissionId, contexts, assessmentConfig);
        return Ok(dto);
    }

    [HttpGet("{instanceId}/{submissionId}/Results/{pageName}")]
    public async Task<ActionResult<AssessmentDto>> GetSubmissionResults(string instanceId, string submissionId,
        string pageName,
        CancellationToken ct)
    {
        var currentUser = await userService.GetCurrentUser(ct);
        if (currentUser == null)
            return Unauthorized();

        var (contexts, assessmentConfig) = await ResolveAssessmentContexts(instanceId, submissionId, ct);

        await Task.WhenAll(contexts.Select(context =>
            rightsService.EnsureAuthorizedForAction(context.Instance, RoleAction.View, context.Form.Name)));

        var dto = assessmentDtoFactory.Create(submissionId, contexts, assessmentConfig, pageName);
        return Ok(dto);
    }

    private async Task<(SubmissionContext[] Contexts, AssessmentConfiguration? assessmentConfig)>
        ResolveAssessmentContexts(
            string instanceId,
            string requestedId,
            CancellationToken ct)
    {
        var instance = await workflowInstanceRepository.GetById(instanceId, ct);
        if (instance == null)
            throw new EntityNotFoundException("WorkflowInstance", instanceId);

        var assessmentConfig = modelService.WorkflowDefinitions[instance.WorkflowDefinition].AssessmentConfiguration;

        // If the form with the name exists, return the single form
        if (modelService.TryGetForm(instance, requestedId) != null)
        {
            var context = await submissionService.GetSubmissionContext(instanceId, requestedId, null, ct);
            return ([context], assessmentConfig);
        }

        // Otherwise treat requestedId as base form and resolve child forms
        var childForms = modelService.GetDerivedForms(instance, requestedId).ToArray();
        if (childForms.Length == 0)
            throw new ArgumentException($"Form with {requestedId} not found");

        var contexts = await Task.WhenAll(
            childForms.Select(f => submissionService.GetSubmissionContext(instanceId, f.Name, null, ct))
        );

        return (contexts, assessmentConfig);
    }
}