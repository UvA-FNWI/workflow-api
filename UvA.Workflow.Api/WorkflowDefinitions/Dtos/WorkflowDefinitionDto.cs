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
        bool canCreateInstance = false,
        Func<Screen, bool>? screenFilter = null)
    {
        var screens = screenFilter == null
            ? workflowDefinition.Screens
            : workflowDefinition.Screens.Where(screenFilter);
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
            screens.Select(ScreenSummaryDto.Create).ToArray(),
            canCreateInstance,
            workflowDefinition.Steps.Count == 0
        );
    }

    /// <summary>Hub / landing: omit parent-scoped related screens (<see cref="Screen.Relation"/>).</summary>
    public static WorkflowDefinitionDto CreateForAccessibleOverview(WorkflowDefinition workflowDefinition) =>
        Create(workflowDefinition, screenFilter: static screen => string.IsNullOrEmpty(screen.Relation));
}