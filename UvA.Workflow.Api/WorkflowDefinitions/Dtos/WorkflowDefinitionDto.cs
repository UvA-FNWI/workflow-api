using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions.Dtos;

public record WorkflowDefinitionDto(
    string Name,
    BilingualString? Title,
    BilingualString TitlePlural,
    BilingualString? Description,
    int? Index,
    Icon? Icon,
    bool IsAlwaysVisible,
    string? InheritsFrom,
    bool IsEmbedded,
    ScreenSummaryDto[] Screens,
    bool CanCreateInstance,
    bool IsPropertyOnly
)
{
    public static WorkflowDefinitionDto Create(WorkflowDefinition workflowDefinition,
        bool canCreateInstance = false)
    {
        return new WorkflowDefinitionDto(
            workflowDefinition.Name,
            workflowDefinition.Title,
            workflowDefinition.TitlePlural,
            workflowDefinition.Description,
            workflowDefinition.Index,
            workflowDefinition.Icon,
            workflowDefinition.IsAlwaysVisible,
            workflowDefinition.InheritsFrom,
            workflowDefinition.IsEmbedded,
            workflowDefinition.Screens.Select(ScreenSummaryDto.Create).ToArray(),
            canCreateInstance,
            workflowDefinition.Steps.Count == 0
        );
    }
}