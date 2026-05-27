using UvA.Workflow.Api.Infrastructure;
using UvA.Workflow.Api.Jobs.Dtos;
using UvA.Workflow.Jobs;
using UvA.Workflow.Users;

namespace UvA.Workflow.Api.Jobs;

public class JobsController(JobService jobService, RightsService rightsService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<JobDto>>> GetList([FromQuery] string? instanceId, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var jobs = await jobService.GetList(instanceId, ct);
        return Ok(jobs.Select(JobDto.Create));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<JobDto>> GetById(string id, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var job = await jobService.GetById(id, ct);
        if (job == null)
            return JobNotFound;

        return Ok(JobDto.Create(job));
    }

    [HttpPost("{id}/run")]
    public async Task<ActionResult<JobDto>> Run(string id, CancellationToken ct)
    {
        await rightsService.EnsureAuthorizedForAction(RoleAction.ViewAdminTools);

        var job = await jobService.GetById(id, ct);
        if (job == null)
            return JobNotFound;

        await jobService.RunJob(job, ct);

        var updatedJob = await jobService.GetById(id, ct);
        return Ok(JobDto.Create(updatedJob!));
    }

    private ObjectResult JobNotFound =>
        NotFound("JobNotFound", "Job not found");
}