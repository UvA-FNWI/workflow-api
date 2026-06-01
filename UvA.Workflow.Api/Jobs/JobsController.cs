using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Jobs.Dtos;
using UvA.Workflow.Jobs;
using UvA.Workflow.Users;

namespace UvA.Workflow.Api.Jobs;

public class JobsController(
    JobService jobService,
    RightsService rightsService,
    IJobRepository jobsRepository,
    IUserRepository userRepository,
    IUserService userService) : ApiControllerBase
{
    [HttpGet("{instanceId}")]
    public async Task<ActionResult<IEnumerable<JobDto>>> GetList(string instanceId, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var jobs = await jobsRepository.GetList(instanceId, ct);
        return Ok(await ToDtos(jobs, ct));
    }

    [HttpGet("{instanceId}/{jobId}")]
    public async Task<ActionResult<JobDto>> GetById(string instanceId, string jobId, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var job = await jobsRepository.GetById(instanceId, jobId, ct);
        if (job == null)
            return JobNotFound;

        return Ok(await ToDto(job, ct));
    }

    [HttpPost("{instanceId}/{jobId}/run")]
    public async Task<ActionResult<JobDto>> Run(string instanceId, string jobId, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var job = await jobsRepository.GetById(instanceId, jobId, ct);
        if (job == null)
            return JobNotFound;

        var user = await userService.GetCurrentUser(ct);
        if (user == null)
            return UserNotFound;

        var copy = CopyJobForRerun(job, user.Id);
        await jobsRepository.Add(copy, ct);
        await jobService.RunJob(copy, ct);

        var updatedJob = await jobsRepository.GetById(instanceId, copy.Id, ct);
        return Ok(await ToDto(updatedJob!, ct));
    }

    private static Job CopyJobForRerun(Job job, string createdBy) => new()
    {
        InstanceId = job.InstanceId,
        SourceType = job.SourceType,
        SourceName = job.SourceName,
        StartOn = DateTime.Now,
        CreatedBy = createdBy,
        Status = JobStatus.Pending,
        Steps = job.Steps.Select(s => new JobStep { Identifier = s.Identifier }).ToList(),
        Input = job.Input,
        IsSynchronous = job.IsSynchronous,
        WorkerGroup = job.WorkerGroup
    };

    private async Task<JobDto> ToDto(Job job, CancellationToken ct)
    {
        var displayName = await ResolveCreatedByDisplayName(job.CreatedBy, ct);
        return JobDto.Create(job, displayName);
    }

    private async Task<IReadOnlyList<JobDto>> ToDtos(IReadOnlyList<Job> jobs, CancellationToken ct)
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
            .Select(job => JobDto.Create(job, ResolveCreatedByDisplayName(job.CreatedBy, displayNamesById)))
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