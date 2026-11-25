using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Jobs.Dtos;
using UvA.Workflow.Jobs;

namespace UvA.Workflow.Api.Jobs;

public class JobsController(
    JobService jobService,
    RightsService rightsService,
    IWorkflowInstanceRepository instanceRepository,
    IJobRepository jobsRepository,
    IUserRepository userRepository,
    IUserService userService) : ApiControllerBase
{
    [HttpGet("{instanceId}")]
    public async Task<ActionResult<IEnumerable<JobDto>>> GetList(string instanceId, CancellationToken ct)
    {
        var canRun = await AuthorizeRead(instanceId, ct);

        var jobs = await jobsRepository.GetList(instanceId, ct);
        return Ok(await ToDtos(jobs, canRun, ct));
    }

    [HttpGet("{instanceId}/{jobId}")]
    public async Task<ActionResult<JobDto>> GetById(string instanceId, string jobId, CancellationToken ct)
    {
        var canRun = await AuthorizeRead(instanceId, ct);

        var job = await jobsRepository.GetById(instanceId, jobId, ct);
        if (job == null)
            return JobNotFound;

        return Ok(await ToDto(job, canRun, ct));
    }

    [HttpPost("{instanceId}/{jobId}/Run")]
    public async Task<ActionResult<JobDto>> Run(string instanceId, string jobId, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var job = await jobsRepository.GetById(instanceId, jobId, ct);
        if (job == null)
            return JobNotFound;

        var user = await userService.GetRealUser(ct);
        if (user == null)
            return UserNotFound;

        var copy = CopyJobForRerun(job, user.Id);
        await jobService.RunNewJob(copy, ct);

        return Ok(await ToDto(copy, canRun: true, ct));
    }

    // Instance-level admin rights allow inspection. Reruns require global admin-tool rights.
    private async Task<bool> AuthorizeRead(string instanceId, CancellationToken ct)
    {
        if (await rightsService.Can(RoleAction.ViewAdminTools))
            return true;

        var instance = await instanceRepository.GetById(instanceId, ct);
        if (instance == null ||
            !await rightsService.Can(instance, [RoleAction.ViewAdminTools], RightsEvaluationMode.RealUser))
            throw new UnauthorizedAccessException();

        return false;
    }

    private static Job CopyJobForRerun(Job job, string createdBy) => new()
    {
        InstanceId = job.InstanceId,
        SourceType = job.SourceType,
        SourceName = job.SourceName,
        StartOn = DateTime.Now,
        CreatedBy = createdBy,
        Steps = job.Steps.Select(s => new JobStep
        {
            Identifier = s.Identifier,
            Status = JobStatus.Pending,
            Outputs = job.Status == JobStatus.Completed
                ? null
                : s.Outputs?.ToDictionary(output => output.Key, output => output.Value)
        }).ToList(),
        Input = job.Input,
        IsSynchronous = job.IsSynchronous,
        WorkerGroup = job.WorkerGroup
    };

    private async Task<JobDto> ToDto(Job job, bool canRun, CancellationToken ct)
    {
        var displayName = await ResolveCreatedByDisplayName(job.CreatedBy, ct);
        return JobDto.Create(job, displayName, canRun);
    }

    private async Task<IReadOnlyList<JobDto>> ToDtos(IReadOnlyList<Job> jobs, bool canRun, CancellationToken ct)
    {
        var creatorIds = jobs
            .Select(j => j.CreatedBy)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .Cast<string>()
            .ToList();

        var displayNamesById = creatorIds.Count == 0
            ? new Dictionary<string, string>()
            : (await userRepository.GetByIds(creatorIds, ct))
            .ToDictionary(u => u.Id, u => u.DisplayName);

        return jobs
            .Select(job => JobDto.Create(job, ResolveCreatedByDisplayName(job.CreatedBy, displayNamesById), canRun))
            .ToList();
    }

    private async Task<string?> ResolveCreatedByDisplayName(string? createdById, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(createdById))
            return null;

        var user = await userRepository.GetById(createdById, ct);
        return user?.DisplayName;
    }

    private static string? ResolveCreatedByDisplayName(string? createdById,
        IReadOnlyDictionary<string, string> displayNamesById)
    {
        if (string.IsNullOrWhiteSpace(createdById))
            return null;

        return displayNamesById.GetValueOrDefault(createdById);
    }

    private ObjectResult JobNotFound =>
        NotFound("JobNotFound", "Job not found");
}