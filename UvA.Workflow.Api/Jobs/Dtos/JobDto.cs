using UvA.Workflow.Jobs;

namespace UvA.Workflow.Api.Jobs.Dtos;

public record JobStepDto(
    string Identifier,
    string? Message,
    Dictionary<string, object>? Outputs)
{
    public static JobStepDto Create(JobStep step) =>
        new(step.Identifier, step.Message, step.Outputs);
}

public record JobDto(
    string Id,
    string InstanceId,
    JobSource SourceType,
    string SourceName,
    DateTime StartOn,
    string? CreatedBy,
    string? CreatedByDisplayName,
    DateTime? ExecutedOn,
    JobStatus Status,
    IReadOnlyList<JobStepDto> Steps,
    bool IsSynchronous,
    string? Message,
    string WorkerGroup,
    DateTime? ClaimedUntil)
{
    public static JobDto Create(Job job, string? createdByDisplayName = null) =>
        new(
            job.Id,
            job.InstanceId,
            job.SourceType,
            job.SourceName,
            job.StartOn,
            job.CreatedBy,
            createdByDisplayName,
            job.ExecutedOn,
            job.Status,
            job.Steps.Select(JobStepDto.Create).ToList(),
            job.IsSynchronous,
            job.Message,
            job.WorkerGroup,
            job.ClaimedUntil);
}