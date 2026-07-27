using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Personal.Dtos;

public record PersonalInstanceDto(
    string Id,
    string WorkflowDefinition,
    BilingualString WorkflowDefinitionTitle,
    string? Title,
    string? CurrentStep,
    DateTime CreatedOn,
    string[] Roles
);