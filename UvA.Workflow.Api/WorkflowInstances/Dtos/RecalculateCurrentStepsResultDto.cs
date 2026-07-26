namespace UvA.Workflow.Api.WorkflowInstances.Dtos;

public record RecalculateCurrentStepsResultDto(
    int Total,
    int Updated,
    int Unchanged
);