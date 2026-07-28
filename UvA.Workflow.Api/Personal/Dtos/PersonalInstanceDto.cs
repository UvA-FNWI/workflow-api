using UvA.Workflow.Api.WorkflowInstances.Dtos;
using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.Personal.Dtos;

public record PersonalInstanceDto(
    string Id,
    string WorkflowDefinition,
    BilingualString WorkflowDefinitionTitle,
    string? Title,
    string? CurrentStep,
    ProgressInformationDto Progress,
    DateTime CreatedOn,
    string[] Roles,
    string? Student,
    string? Course,
    string[] Employees
);