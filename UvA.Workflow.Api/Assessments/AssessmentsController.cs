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

        return Ok(await CreateResults(instance, assessmentConfig, ct));
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
            return Ok(await CreateResults(instance, new AssessmentConfiguration
            {
                Parts = [matchingPart],
                GradingBasis = assessmentConfig!.GradingBasis,
                GradeGap = assessmentConfig.GradeGap
            }, ct));
        }

        var context = await workflowInstanceService.GetSubmissionContext(instanceId, submissionId, null, ct);
        await rightsService.EnsureAuthorizedForAction(context.Instance, RoleAction.View, context.Form.Name);
        var dto = assessmentDtoFactory.Create(instance, [context], assessmentConfig);
        return Ok(dto);
    }

    private async Task<AssessmentDto> CreateResults(
        WorkflowInstance instance,
        AssessmentConfiguration? config,
        CancellationToken ct)
    {
        var context = await instanceService.ResolveContext(instance, ct);
        var parts = config?.Parts.Select(part =>
            context.ImportedSteps.GetValueOrDefault(part.Name) is { } owner
                ? (Instance: owner.Instance, Part: owner.Definition.AssessmentConfiguration!.Parts
                    .Single(source => source.Name == owner.Step.Name))
                : (Instance: instance, Part: part));
        var contexts = new List<SubmissionContext>();
        var allowedForms = new HashSet<(string InstanceId, string Form)>();
        foreach (var group in parts?.GroupBy(part => part.Instance.Id) ?? [])
        {
            var sourceInstance = group.First().Instance;
            var sourceNames = group.SelectMany(part => part.Part.Sources).Select(source => source.Name).ToHashSet();
            if (sourceInstance.Id != instance.Id)
                foreach (var name in sourceNames)
                    await rightsService.EnsureAuthorizedForAction(sourceInstance,
                        [RoleAction.View, RoleAction.ViewResults], RightsEvaluationMode.RequestContext, name);

            var submissions = await instanceService.GetAllowedSubmissions(sourceInstance, ct, true);
            foreach (var submission in submissions.Where(submission => sourceNames.Contains(submission.Form.Name)))
            {
                contexts.AddRange(await assessmentService.ResolveAssessmentContexts(sourceInstance,
                    submission.Form.Name, ct));
                if (submission.CanView) allowedForms.Add((sourceInstance.Id, submission.Form.Name));
            }
        }

        return assessmentDtoFactory.Create(instance, contexts, config,
            allowedForms: allowedForms, resolvedContext: context);
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