using UvA.Workflow.Jobs;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Actions.Dtos;

public record ExecuteActionInputDto(
    ActionType Type,
    string InstanceId,
    string? Name = null,
    JobInput? JobInput = null
);