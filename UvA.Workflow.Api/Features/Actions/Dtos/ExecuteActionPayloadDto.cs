using UvA.Workflow.Api.Features.WorkflowInstances.Dtos;

namespace UvA.Workflow.Api.Features.Actions.Dtos;

public record ExecuteActionPayloadDto(
    ActionType Type,
    WorkflowInstanceDto? Instance
);