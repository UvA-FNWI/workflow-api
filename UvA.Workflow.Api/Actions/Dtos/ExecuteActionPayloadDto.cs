using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Actions.Dtos;

public record ExecuteActionPayloadDto(
    ActionType Type,
    WorkflowInstanceDto? Instance,
    EffectResult Result
);