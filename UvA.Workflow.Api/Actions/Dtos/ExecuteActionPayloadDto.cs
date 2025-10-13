using UvA.Workflow.Api.WorkflowInstances.Dtos;

namespace UvA.Workflow.Api.Actions.Dtos;

public record ExecuteActionPayloadDto(
    ActionType Type,
    WorkflowInstanceDto? Instance
);