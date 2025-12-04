using UvA.Workflow.WorkflowModel;

namespace UvA.Workflow.Api.WorkflowDefinitions.Dtos;

public record WorkflowDefinitionDto(
    string Name,
    BilingualString? Title,
    BilingualString TitlePlural,
    int? Index,
    bool IsAlwaysVisible,
    string? InheritsFrom,
    bool IsEmbedded,
    string[] Screens
)
{
    public static WorkflowDefinitionDto Create(WorkflowDefinition workflowDefinition)
    {
        return new WorkflowDefinitionDto(
            workflowDefinition.Name,
            workflowDefinition.Title,
            workflowDefinition.TitlePlural,
            workflowDefinition.Index,
            workflowDefinition.IsAlwaysVisible,
            workflowDefinition.InheritsFrom,
            workflowDefinition.IsEmbedded,
            workflowDefinition.Screens.Select(s => s.Name).ToArray()
        );
    }
}