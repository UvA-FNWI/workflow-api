using UvA.Workflow.Jobs;

namespace UvA.Workflow.Api.Actions.Dtos;

public record ExecuteActionInputDto(
    ActionType Type,
    string InstanceId,
    string? Name = null,
    JobInput? JobInput = null
);